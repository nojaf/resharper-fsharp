﻿namespace JetBrains.ReSharper.Plugins.FSharp.Checker

#nowarn "57"

open System
open System.Collections.Generic
open System.Threading.Tasks
open FSharp.Compiler.CodeAnalysis
open JetBrains.Application
open JetBrains.Application.BuildScript.Application.Zones
open JetBrains.Diagnostics
open JetBrains.ProjectModel
open JetBrains.ProjectModel.MSBuild
open JetBrains.ProjectModel.ProjectsHost
open JetBrains.ProjectModel.ProjectsHost.MsBuild.Strategies
open JetBrains.ProjectModel.ProjectsHost.SolutionHost
open JetBrains.ProjectModel.Properties.Managed
open JetBrains.ReSharper.Plugins.FSharp.ProjectModel
open JetBrains.ReSharper.Plugins.FSharp.ProjectModel.Host.ProjectItems.ItemsContainer
open JetBrains.ReSharper.Plugins.FSharp.Shim.AssemblyReader
open JetBrains.ReSharper.Plugins.FSharp.Util
open JetBrains.ReSharper.Psi.Modules
open JetBrains.ReSharper.Resources.Shell
open JetBrains.Util
open JetBrains.Util.Dotnet.TargetFrameworkIds

[<SolutionInstanceComponent>]
[<ZoneMarker(typeof<IHostSolutionZone>)>]
type FSharpTargetsProjectLoadModificator() =
    let fsTargets =
        [| "GenerateCode"
           "GenerateFSharpInternalsVisibleToFile"
           "GenerateAssemblyFileVersionTask"
           "ImplicitlyExpandNETStandardFacades" |]

    interface MsBuildLegacyLoadStrategy.IModificator with
        member x.IsApplicable(mark) =
            match mark with
            | FSharpProjectMark -> true
            | _ -> false

        member x.ModifyTargets(targets) =
            targets.AddRange(fsTargets)

        member x.ModifyProperties _ =
            ()

module FcsProjectBuilder =
    let itemsDelimiters = [| ';'; ','; ' ' |]

    let splitAndTrim (delimiters: char[]) (s: string) =
        if isNull s then EmptyArray.Instance else
        s.Split(delimiters, StringSplitOptions.RemoveEmptyEntries)

    let getProjectConfiguration (targetFramework: TargetFrameworkId) (project: IProject) =
        let projectProperties = project.ProjectProperties
        projectProperties.ActiveConfigurations.TryGetConfiguration(targetFramework).As<IManagedProjectConfiguration>()

    let getDefines (configuration: IManagedProjectConfiguration) =
        if isNull configuration then [] else

        splitAndTrim itemsDelimiters configuration.DefineConstants
        |> List.ofArray

[<SolutionComponent>]
[<ZoneMarker(typeof<ISinceClr4HostZone>)>]
type FcsProjectBuilder(checkerService: FcsCheckerService, itemsContainer: IFSharpItemsContainer,
        modulePathProvider: ModulePathProvider, logger: ILogger, psiModules: IPsiModules) =

    let defaultOptions =
        [| "--noframework"
           "--debug:full"
           "--debug+"
           "--optimize-"
           "--tailcalls-"
           "--fullpaths"
           "--highentropyva+"
           "--noconditionalerasure"
           "--ignorelinedirectives" |]

    let unusedValuesWarns =
        [| "--warnon:1182" |]

    let xmlDocsNoWarns =
        [| "--nowarn:3390" |]

    let getOutputType (outputType: ProjectOutputType) =
        match outputType with
        | ProjectOutputType.CONSOLE_EXE -> "exe"
        | ProjectOutputType.WIN_EXE -> "winexe"
        | ProjectOutputType.MODULE -> "module"
        | _ -> "library"

    abstract GetProjectItemsPaths:
        project: IProject * targetFrameworkId: TargetFrameworkId -> (VirtualFileSystemPath * BuildAction)[]

    default x.GetProjectItemsPaths(project, targetFrameworkId) =
        let projectMark = project.GetProjectMark().NotNull()
        itemsContainer.GetProjectItemsPaths(projectMark, targetFrameworkId)

    member x.GetProjectFilesAndResources(project: IProject, targetFrameworkId) =
        let sourceFiles = List()
        let resources = List()

        let sigFiles = HashSet()
        let implsWithSigs = HashSet()

        let projectItems = x.GetProjectItemsPaths(project, targetFrameworkId)

        for path, buildAction in projectItems do
            match buildAction with
            | SourceFile ->
                sourceFiles.Add(path)
                let fileName = path.NameWithoutExtension
                match path.ExtensionNoDot with
                | SigExtension -> sigFiles.Add(fileName) |> ignore
                | ImplExtension when sigFiles.Contains(fileName) -> implsWithSigs.add(path)
                | _ -> ()

            | Resource -> resources.Add(path)
            | _ -> ()

        let resources: IList<_> = if resources.IsEmpty() then EmptyList.InstanceList else resources :> _
        let implsWithSigs: ISet<_> = if implsWithSigs.IsEmpty() then EmptySet.Instance :> _ else implsWithSigs :> _

        sourceFiles.ToArray(), implsWithSigs, resources

    member x.BuildFcsProject(projectKey: FcsProjectKey): FcsProject =
        let project = projectKey.Project
        let targetFrameworkId = projectKey.TargetFrameworkId

        let projectProperties = project.ProjectProperties

        let otherOptions = List()

        let outPath = project.GetOutputFilePath(targetFrameworkId)
        if not outPath.IsEmpty then
            otherOptions.Add("--out:" + outPath.FullPath)

        otherOptions.AddRange(defaultOptions)
        otherOptions.AddRange(unusedValuesWarns)
        otherOptions.AddRange(xmlDocsNoWarns)

        match projectProperties.ActiveConfigurations.TryGetConfiguration(targetFrameworkId) with
        | :? IManagedProjectConfiguration as cfg ->
            let definedConstants = FcsProjectBuilder.getDefines cfg
            otherOptions.AddRange(definedConstants |> Seq.map (fun c -> "--define:" + c))

            otherOptions.Add($"--target:{getOutputType cfg.OutputType}")

            otherOptions.Add$"--warn:{cfg.WarningLevel}"

            if cfg.TreatWarningsAsErrors then
                otherOptions.Add("--warnaserror")

            if Shell.Instance.IsTestShell then
                let psiModule = psiModules.GetPrimaryPsiModule(project, targetFrameworkId)
                let languageLevel = FSharpLanguageLevel.ofPsiModuleNoCache psiModule
                let langVersionArg =
                    languageLevel
                    |> FSharpLanguageLevel.toLanguageVersion
                    |> FSharpLanguageVersion.toCompilerArg

                otherOptions.Add(langVersionArg)

            let doc = cfg.DocumentationFile
            if not (doc.IsNullOrWhitespace()) then otherOptions.Add("--doc:" + doc)

            let props = cfg.PropertiesCollection

            let getOption f (p: string, compilerArg) =
                let compilerArg = defaultArg compilerArg (p.ToLower())
                match props.TryGetValue(p) with
                | true, v when not (v.IsNullOrWhitespace()) -> Some ("--" + compilerArg + ":" + f v)
                | _ -> None

            [ FSharpProperties.TargetProfile, None; FSharpProperties.LangVersion, None ]
            |> List.choose (getOption id)
            |> otherOptions.AddRange

            [ FSharpProperties.NoWarn, None
              MSBuildProjectUtil.WarningsAsErrorsProperty, Some("warnaserror")
              MSBuildProjectUtil.WarningsNotAsErrorsProperty, Some("warnaserror-") ]
            |> List.choose (getOption (fun v -> (FcsProjectBuilder.splitAndTrim FcsProjectBuilder.itemsDelimiters v).Join(",")))
            |> otherOptions.AddRange

            match props.TryGetValue(FSharpProperties.OtherFlags) with
            | true, otherFlags when not (otherFlags.IsNullOrWhitespace()) -> FcsProjectBuilder.splitAndTrim [| ' ' |] otherFlags
            | _ -> EmptyArray.Instance
            |> otherOptions.AddRange
        | _ -> ()

        let filePaths, implsWithSig, resources = x.GetProjectFilesAndResources(project, targetFrameworkId)

        otherOptions.AddRange(resources |> Seq.map (fun (r: VirtualFileSystemPath) -> "--resource:" + r.FullPath))
        let fileIndices = Dictionary<VirtualFileSystemPath, int>()
        Array.iteri (fun i p -> fileIndices[p] <- i) filePaths

        let psiModule = psiModules.GetPrimaryPsiModule(project, targetFrameworkId)
        
        let sourceFiles =
            psiModule.SourceFiles
            |> Seq.map (fun psiSourceFile ->
                // TODO: I assume the Create will expect the full path of the file?
                let name = psiSourceFile.Name
                let version = string psiSourceFile.Document.LastModificationStamp.Value
                let getSource () = psiSourceFile.Document.GetText() |> FSharp.Compiler.Text.SourceTextNew.ofString |> Task.FromResult
                ProjectSnapshot.FSharpFileSnapshot.Create(name, version, getSource)
            )
            |> Seq.toList
        
        let references = projectKey.Project.GetModuleReferences(projectKey.TargetFrameworkId)
        let referencesOnDisk: ProjectSnapshot.ReferenceOnDisk list =
            references
            |> Seq.choose (fun projectToModuleReference ->
                projectToModuleReference
                |> modulePathProvider.GetModulePath
                |> Option.bind (fun path ->
                    if path.IsEmpty then
                        None
                    else
                        Some ({
                            Path = path.FullPath
                            LastModified = path.FileModificationTimeUtc
                        } : ProjectSnapshot.ReferenceOnDisk))
            )
            |> Seq.toList

        let otherOptions = Seq.toList otherOptions
        
        let projectSnapshot =
            FSharpProjectSnapshot.Create(
                projectFileName = $"{project.ProjectFileLocation}.{targetFrameworkId}.fsproj",
                projectId = None,
                sourceFiles = sourceFiles,
                referencesOnDisk = referencesOnDisk,
                otherOptions = Seq.toList otherOptions,
                referencedProjects = List.empty,
                isIncompleteTypeCheckEnvironment = false,
                useScriptResolutionRules = false,
                loadTime = DateTime.Now,
                unresolvedReferences = None,
                originalLoadReferences = List.empty,
                stamp = None
            )

        let parsingOptions, errors =
            checkerService.Checker.GetParsingOptionsFromCommandLineArgs(otherOptions)

        let defines = ImplicitDefines.sourceDefines @ parsingOptions.ConditionalDefines

        let parsingOptions = { parsingOptions with
                                 SourceFiles = sourceFiles |> List.map (fun sf -> sf.FileName) |> Array.ofList
                                 ConditionalDefines = defines }

        if not errors.IsEmpty then
            logger.Warn("Getting parsing options: {0}", concatErrors errors)

        let fcsProject =
            { OutputPath = outPath
              ProjectSnapshot = projectSnapshot 
              ParsingOptions = parsingOptions
              FileIndices = fileIndices
              ImplementationFilesWithSignatures = implsWithSig
              ReferencedModules = HashSet() }

        fcsProject
