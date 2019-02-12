﻿using System.Collections.Generic;
using JetBrains.Annotations;
using JetBrains.ReSharper.Plugins.FSharp.Psi.Impl.DeclaredElement;
using JetBrains.ReSharper.Plugins.FSharp.Psi.Tree;
using JetBrains.ReSharper.Psi;
using JetBrains.ReSharper.Psi.ExtensionsAPI;
using JetBrains.ReSharper.Psi.Tree;
using Microsoft.FSharp.Compiler.SourceCodeServices;

namespace JetBrains.ReSharper.Plugins.FSharp.Psi.Impl.Tree
{
  internal partial class UnionCaseFieldDeclaration
  {
    public override IFSharpIdentifier NameIdentifier => (IFSharpIdentifier) Identifier;

    protected override string BaseName => "Item";
    protected override int BaseIndex => 1;

    protected override bool UseBaseNameForSingleField => true;

    protected override TreeNodeCollection<ICaseFieldDeclaration> GetFields() =>
      Parent is INestedTypeUnionCaseDeclaration unionCaseDeclaration
        ? unionCaseDeclaration.Fields
        : TreeNodeCollection<ICaseFieldDeclaration>.Empty;

    protected override IDeclaredElement CreateDeclaredElement(ITypeMemberDeclaration declaration, FSharpField fsField) =>
      new FSharpUnionCaseField<UnionCaseFieldDeclaration>(declaration, fsField);
  }

  internal partial class ExceptionFieldDeclaration
  {
    public override IFSharpIdentifier NameIdentifier => (IFSharpIdentifier) Identifier;

    protected override string BaseName => "Data";
    protected override int BaseIndex => 0;

    protected override bool UseBaseNameForSingleField => false;

    protected override TreeNodeCollection<ICaseFieldDeclaration> GetFields() =>
      Parent is IExceptionDeclaration exceptionDeclaration
        ? exceptionDeclaration.Fields
        : TreeNodeCollection<ICaseFieldDeclaration>.Empty;

    protected override IDeclaredElement CreateDeclaredElement(ITypeMemberDeclaration declaration, FSharpField fsField) =>
      new FSharpUnionCaseField<ExceptionFieldDeclaration>(declaration, fsField);
  }

  internal abstract class CaseFieldDeclarationBase : FSharpProperTypeMemberDeclarationBase
  {
    protected override string DeclaredElementName
    {
      get
      {
        var id = NameIdentifier;
        if (id != null)
          return id.GetSourceName();

        var fields = GetFields();
        if (UseBaseNameForSingleField && fields.Count == 1)
          return BaseName;

        var fieldIndex = fields.IndexOf((ICaseFieldDeclaration) this);
        return fieldIndex == -1
          ? SharedImplUtil.MISSING_DECLARATION_NAME
          : BaseName + (BaseIndex + fieldIndex);
      }
    }

    public int GetIndex() => GetIndex(GetFields());

    protected int GetIndex(TreeNodeCollection<ICaseFieldDeclaration> fields) =>
      fields.IndexOf((ICaseFieldDeclaration) this);

    protected abstract int BaseIndex { get; }
    protected abstract string BaseName { get; }
    protected abstract bool UseBaseNameForSingleField { get; }
    protected abstract TreeNodeCollection<ICaseFieldDeclaration> GetFields();

    protected abstract IDeclaredElement CreateDeclaredElement(ITypeMemberDeclaration declaration,
      FSharpField fsField);

    protected override IDeclaredElement CreateDeclaredElement()
    {
      var typeDeclaration = Parent as IFSharpTypeDeclaration;
      var typeSymbol = typeDeclaration?.GetFSharpSymbol();
      if (typeSymbol == null)
        return null;

      var index = GetIndex();
      var fields = GetFieldsSymbols(typeSymbol);
      var caseField =
        fields != null && index <= fields.Count
          ? fields[index]
          : null;

      return caseField != null
        ? CreateDeclaredElement(this, caseField)
        : null;
    }

    private static IList<FSharpField> GetFieldsSymbols([NotNull] FSharpSymbol typeSymbol)
    {
      switch (typeSymbol)
      {
        case FSharpUnionCase unionCase:
          return unionCase.UnionCaseFields;
        case FSharpEntity entity when entity.IsFSharpExceptionDeclaration:
          return entity.FSharpFields;
        default:
          return null;
      }
    }

    public bool IsNameGenerated => NameIdentifier != null;
  }
}
