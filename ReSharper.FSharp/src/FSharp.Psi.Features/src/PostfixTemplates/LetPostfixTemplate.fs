namespace JetBrains.ReSharper.Plugins.FSharp.Psi.Features.PostfixTemplates

open JetBrains.ReSharper.Feature.Services.LiveTemplates.LiveTemplates
open JetBrains.ReSharper.Feature.Services.LiveTemplates.Hotspots
open JetBrains.ReSharper.Feature.Services.LiveTemplates.Templates
open JetBrains.ReSharper.Feature.Services.PostfixTemplates
open JetBrains.ReSharper.Feature.Services.PostfixTemplates.Contexts
open JetBrains.ReSharper.Plugins.FSharp.Psi
open JetBrains.ReSharper.Plugins.FSharp.Psi.Features.Refactorings
open JetBrains.ReSharper.Plugins.FSharp.Psi.Features.Util
open JetBrains.ReSharper.Plugins.FSharp.Psi.Tree
open JetBrains.ReSharper.Psi.ExtensionsAPI
open JetBrains.ReSharper.Psi.ExtensionsAPI.Tree
open JetBrains.ReSharper.Psi.Transactions
open JetBrains.ReSharper.Psi.Tree
open JetBrains.ReSharper.Resources.Shell
open JetBrains.Util

[<PostfixTemplate("let", "Introduce let binding", "let _ = expr")>]
type LetPostfixTemplate() =
    interface IPostfixTemplate with
        member x.Language = FSharpLanguage.Instance :> _
        member x.CreateBehavior(info) = LetPostfixTemplateBehavior(info) :> _

        member x.TryCreateInfo(context) =
            LetPostfixTemplateInfo(context.AllExpressions.[0]) :> _


and LetPostfixTemplateInfo(expressionContext: PostfixExpressionContext) =
    inherit PostfixTemplateInfo("let", expressionContext)


and LetPostfixTemplateBehavior(info) as this =
    inherit FSharpPostfixTemplateBehaviorBase(info)

    static member val PreventIntroduceVarKey = Key("PreventIntroduceVarKey")

    override x.ExpandPostfix(context) =
        let psiModule = context.PostfixContext.PsiModule
        let psiServices = psiModule.GetPsiServices()

        // todo: top level bindings should be created as module members, not as let expressions
        psiServices.Transactions.Execute(x.ExpandCommandName, fun _ ->
            let node = context.Expression :?> IFSharpTreeNode
            let elementFactory = node.CreateElementFactory()
            use writeCookie = WriteLockCookie.Create(node.IsPhysical())
            use disableFormatter = new DisableCodeFormatter()
            let expr = this.GetExpression(context)

            if (FSharpIntroduceVariable.CanIntroduceVar(expr)) then expr :> ITreeNode else

            let letOrUseExpr = elementFactory.CreateLetBindingExpr("_")
            setBindingExpression expr expr.Indent letOrUseExpr
            let replaced = ModificationUtil.ReplaceChild(expr, letOrUseExpr)
            replaced.UserData.PutKey(LetPostfixTemplateBehavior.PreventIntroduceVarKey)
            replaced :> _)

    override x.AfterComplete(textControl, node, _) =
        if not (node.UserData.HasKey(LetPostfixTemplateBehavior.PreventIntroduceVarKey)) then
            FSharpIntroduceVariable.IntroduceVar(node :?> _, textControl, false, false) else

        let letExpr = node.As<ILetOrUseExpr>()
        if isNull letExpr then () else

        let hotspotInfos =
            let headPattern = letExpr.Bindings.[0].HeadPattern
            let templateField = TemplateField("Foo", SimpleHotspotExpression(null), 0)
            HotspotInfo(templateField, headPattern.GetDocumentRange(), KeepExistingText = true)

        let hotspotSession =
            LiveTemplatesManager.Instance.CreateHotspotSessionAtopExistingText(
                info.ExecutionContext.Solution, letExpr.GetDocumentEndOffset(), textControl,
                LiveTemplatesManager.EscapeAction.LeaveTextAndCaret, hotspotInfos)

        hotspotSession.ExecuteAndForget()
