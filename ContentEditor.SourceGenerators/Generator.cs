namespace RszTool.Generators;

using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

[Generator(LanguageNames.CSharp)]
public class ContentEditorGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // var classes = context.SyntaxProvider
        //     .CreateSyntaxProvider<HashedFieldConstGeneratorCtx>(
        //         predicate: static (s, _) => s is ClassDeclarationSyntax classDecl && classDecl.Identifier.Text == "ConfigKeys",
        //         transform: static (ctx, _) => new HashedFieldConstGeneratorCtx() {
        //             ClassDecl = (ClassDeclarationSyntax)ctx.Node,
        //             Fields = ctx.Node.ChildNodes().OfType<FieldDeclarationSyntax>().Where(field => field.GetFieldType()?.GetElementTypeName() == "HashedString").ToList(),
        //         }
        //     )
        //     .Where(ctx => ctx is not null);

        // context.RegisterSourceOutput(classes, static (ctx, source) => {
        //     var clsName = source.ClassDecl.Identifier.Text;
        //     var sb = new StringBuilder();
        //     AppendClassDefinition(sb, source.ClassDecl, out int indent);
        //     var indentStr = new string('\t', indent);
        //     foreach (var field in source.Fields) {
        //         var name = field.GetFieldName();
        //         if (name == null) continue;
        //         sb.Append(indentStr).AppendLine($"public const uint {name}_Hash = {MurMur3HashUtils.GetHash(name)};");
        //     }
        //     CloseIndents(sb, indent);
        //     ctx.AddSource($"{clsName}.g", sb.ToString());
        // });
    }

    private static void AppendClassDefinition(StringBuilder sb, ClassDeclarationSyntax classDecl, out int indentCount)
    {
        var ns = classDecl.GetFullNamespace();
        if (ns != null) sb.Append("namespace ").Append(ns).Append(';').AppendLine();
        sb.AppendLine();

        var usings = classDecl.SyntaxTree.GetRoot().ChildNodes().OfType<UsingDirectiveSyntax>();
        if (usings != null) foreach (var uu in usings) {
            sb.AppendLine(uu.GetText().ToString().TrimEnd());
        }
        sb.AppendLine();
        sb.AppendLine("// wtf");

        var parents = classDecl.Ancestors().OfType<ClassDeclarationSyntax>().Reverse();
        foreach (var parent in parents) {
            sb.Append(string.Join(" ", parent.Modifiers.Select(m => m.Text))).Append(" class ").AppendLine(parent.Identifier.Text).AppendLine("{");
        }

        indentCount = parents.Count() + 1;
        var classIndent = new string('\t', indentCount - 1);

        sb.Append(classIndent).Append(string.Join(" ", classDecl.Modifiers.Select(m => m.Text))).Append(" class ").Append(classDecl.Identifier.Text);
        sb.Append(classDecl.TypeParameterList?.ToString() ?? string.Empty);
        sb.Append(classDecl.BaseList?.ToString() ?? string.Empty);
        sb.AppendLine();
        sb.Append(classIndent).AppendLine("{");
    }

    private static void CloseIndents(StringBuilder sb, int indents)
    {
        for (int i = indents - 1; i >= 0; --i) sb.Append(new string('\t', indents - 1)).AppendLine("}");
    }
}

public class HashedFieldConstGeneratorCtx
{
    public ClassDeclarationSyntax ClassDecl = null!;
    public List<FieldDeclarationSyntax> Fields = new();
}
