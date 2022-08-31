namespace JetBrains.ReSharper.Plugins.FSharp.Psi.Features.Intentions

open JetBrains.ReSharper.Feature.Services.ContextActions
open JetBrains.ReSharper.Plugins.FSharp.Psi
open JetBrains.ReSharper.Plugins.FSharp.Psi.Impl.Tree
open JetBrains.ReSharper.Plugins.FSharp.Psi.Tree
open JetBrains.ReSharper.Psi.ExtensionsAPI
open JetBrains.ReSharper.Psi.ExtensionsAPI.Tree
open JetBrains.ReSharper.Psi.Tree
open JetBrains.ReSharper.Resources.Shell
open JetBrains.ReSharper.Plugins.FSharp.Psi.Services.Util

[<ContextAction(Name = "AddFunctionToSignatureFile", Group = "F#", Description = "Add function to signature file")>]
type AddFunctionToSignatureFileAction(dataProvider: FSharpContextActionDataProvider) =
    inherit ContextActionBase()
    
    let getSignatureLocation () =
        let letBindings = dataProvider.GetSelectedElement<ILetBindings>()
        if isNull letBindings then None else

        let bindings = letBindings.Bindings
        if bindings.Count <> 1 then None else
        
        match bindings.First().HeadPattern.As<IReferencePat>() with
        | null -> None
        | pat -> pat.GetFcsSymbol().SignatureLocation

    let addChildAfter child anchor = ModificationUtil.AddChildAfter(anchor, child)
    
    override x.Text = "Add to signature file"
    override this.IsAvailable _ =
        let currentFSharpFile = dataProvider.PsiFile
        let fcsService = currentFSharpFile.FcsCheckerService
        let hasSignature = fcsService.FcsProjectProvider.HasPairFile dataProvider.SourceFile
        if not hasSignature then false else
        
        match getSignatureLocation () with
        | None -> false
        | Some range -> not (range.FileName.EndsWith(".fsi"))

    override this.ExecutePsiTransaction(solution, progress) =
        use writeCookie = WriteLockCookie.Create(true)
        use disableFormatter = new DisableCodeFormatter()
        let letBindings = dataProvider.GetSelectedElement<ILetBindingsDeclaration>()
        let moduleDecl = ModuleDeclarationNavigator.GetByMember(letBindings)

        match SignatureFile.tryMkBindingSignature letBindings moduleDecl with
        | None -> null
        | Some (sigDeclNode, openStatements, sigFile) ->
            let newlineNode = NewLine(sigDeclNode.GetLineEnding()) :> ITreeNode
            
            sigFile.LastChild
            |> addChildAfter newlineNode
            |> fun lastChild ->
                if List.isEmpty openStatements then
                    lastChild
                else
                    (lastChild, openStatements)
                    ||> List.fold (fun acc openStatement -> addChildAfter openStatement acc |> addChildAfter newlineNode)
                    |> addChildAfter newlineNode
            |> addChildAfter sigDeclNode
            |> addChildAfter newlineNode
            |> ignore

            null