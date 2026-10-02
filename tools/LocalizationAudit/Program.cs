using System.Text.Json;
using System.Text.RegularExpressions;
using ContentEditor.App;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using VYaml.Serialization;

if (args.Length < 2 || (args.Contains("--check") && args.Contains("--rewrite"))) {
    Console.Error.WriteLine("Usage: LocalizationAudit <repository> <report-directory> [--check | --rewrite]");
    Environment.ExitCode = 2;
    return;
}
var root = Path.GetFullPath(args[0]);
var output = Path.GetFullPath(args[1]);
var rewrite = args.Contains("--rewrite");
var entries = new SortedDictionary<string, HashSet<string>>(StringComparer.Ordinal);
var remaining = new SortedDictionary<string, HashSet<string>>(StringComparer.Ordinal);
foreach (var directory in new[] { "ContentEditor.App", "ContentEditor.Core" }) {
    foreach (var file in Directory.EnumerateFiles(Path.Combine(root, directory), "*.cs", SearchOption.AllDirectories)) {
        if (file.Split(Path.DirectorySeparatorChar).Any(p => p is "obj" or "bin") || file.Contains("i18n") || file.EndsWith("UiText.cs")) continue;
        var source = File.ReadAllText(file);
        var tree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
        var visitor = new TextVisitor(entries, Path.GetRelativePath(root, file));
        var updated = visitor.Visit(tree.GetRoot())!;
        foreach (var literal in updated.DescendantNodes().OfType<LiteralExpressionSyntax>()) {
            if (!literal.IsKind(SyntaxKind.StringLiteralExpression) && !literal.IsKind(SyntaxKind.Utf8StringLiteralExpression)) continue;
            var value = literal.Token.ValueText;
            if (!Regex.IsMatch(value, "[A-Za-z]{3}") || value.StartsWith("##")) continue;
            if (literal.Ancestors().OfType<InvocationExpressionSyntax>().Any(x => x.Expression.ToString().StartsWith("UiText."))) continue;
            if (!remaining.TryGetValue(value, out var locations)) remaining[value] = locations = new();
            locations.Add(Path.GetRelativePath(root, file) + ":" + (literal.GetLocation().GetLineSpan().StartLinePosition.Line + 1));
        }
        if (rewrite && updated.ToFullString() != source) File.WriteAllText(file, updated.ToFullString());
    }
}
Directory.CreateDirectory(output);
var options = new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
File.WriteAllText(Path.Combine(output, "ui-source.json"), JsonSerializer.Serialize(entries, options));
File.WriteAllText(Path.Combine(output, "lang-source.json"), JsonSerializer.Serialize(Lang.GetTranslationsJson(), options));
File.WriteAllText(Path.Combine(output, "remaining-source.json"), JsonSerializer.Serialize(remaining, options));
Console.WriteLine($"UI templates: {entries.Count}; Lang entries: {Lang.GetTranslationsJson().Count}; rewrite: {rewrite}");
var packDirectory = Path.Combine(root, "ContentEditor.App", "i18n");
var uiTranslations = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(Path.Combine(packDirectory, "SimplifiedChinese.ui.json")))!;
var registeredTranslations = YamlSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllBytes(Path.Combine(packDirectory, "SimplifiedChinese.lang.yaml")))!;
static string Normalize(string source) => source.Replace("\r\n", "\n", StringComparison.Ordinal);
var uiKeys = uiTranslations.Keys.Select(Normalize).ToHashSet(StringComparer.Ordinal);
var missingUi = entries.Keys.Where(key => !uiKeys.Contains(Normalize(key))).ToArray();
var missingRegistered = Lang.GetTranslationsJson().Keys.Except(registeredTranslations.Keys).ToArray();
var unchangedUi = uiTranslations.Where(pair => pair.Key == pair.Value).Select(pair => pair.Key).ToArray();
File.WriteAllText(Path.Combine(output, "coverage.json"), JsonSerializer.Serialize(new {
    registeredSourceCount = Lang.GetTranslationsJson().Count,
    registeredTranslationCount = registeredTranslations.Count,
    uiSourceCount = entries.Count,
    uiTranslationCount = uiTranslations.Count,
    missingRegistered,
    missingUi,
    unchangedUi,
    note = "Unchanged terms and remaining-source.json require human review; source coverage does not establish runtime visual coverage."
}, options));
Console.WriteLine($"Missing registered: {missingRegistered.Length}; missing UI: {missingUi.Length}; unchanged terms for review: {unchangedUi.Length}");
if (args.Contains("--check") && (missingUi.Length > 0 || missingRegistered.Length > 0)) Environment.ExitCode = 1;

sealed class TextVisitor(SortedDictionary<string, HashSet<string>> entries, string file) : CSharpSyntaxRewriter
{
    private static readonly HashSet<string> labels = new("Button SmallButton InvisibleButton Checkbox CheckboxFlags RadioButton Selectable BeginMenu MenuItem BeginTabItem CollapsingHeader TreeNode TreeNodeEx TableSetupColumn BeginCombo InputText InputTextMultiline InputTextWithHint InputInt InputInt2 InputInt3 InputInt4 InputFloat InputDouble InputFloat2 InputFloat3 InputFloat4 InputScalar DragFloat DragFloat2 DragFloat3 DragFloat4 DragInt DragInt2 DragInt3 DragInt4 DragScalar SliderFloat SliderFloat2 SliderFloat3 SliderFloat4 SliderInt SliderAngle VSliderFloat ColorEdit3 ColorEdit4 ColorPicker3 ColorPicker4".Split(' '));
    private static readonly HashSet<string> texts = new("Text TextUnformatted TextWrapped TextDisabled SeparatorText SetTooltip SetItemTooltip CalcTextSize".Split(' '));
    private static readonly HashSet<string> helperLabels = new("ValueCombo CSharpEnumCombo EnumCombo FilterableCombo FilterableEntityCombo FilterableCSharpEnumCombo TreeNodeSuffix InputScalar ToggleButton TextMultilineAutoResize".Split(' '));
    private static readonly HashSet<string> helperTexts = new("Tooltip TooltipColored TextCentered TextSuffix SelectableSuffix".Split(' '));

    public override SyntaxNode? VisitInvocationExpression(InvocationExpressionSyntax node)
    {
        // Existing wrappers are catalogued too, making the audit repeatable.
        var expression = node.Expression.ToString();
        if (expression.StartsWith("UiText.")) {
            if (node.ArgumentList.Arguments.Count > 0) Record(node.ArgumentList.Arguments[0].Expression, expression.Contains("Label"));
            return node;
        }
        var result = (InvocationExpressionSyntax)base.VisitInvocationExpression(node)!;
        var owner = node.Expression is MemberAccessExpressionSyntax access ? access.Expression.ToString() : "";
        var name = node.Expression switch {
            MemberAccessExpressionSyntax member => member.Name.Identifier.ValueText,
            MemberBindingExpressionSyntax binding => binding.Name.Identifier.ValueText,
            _ => ""
        };
        if (name.Length == 0) return result;
        if (name is "AddChild" or "AddChildList" or "CreateRootContext" or "GetChild") {
            if (result.ArgumentList.Arguments.Count > 0) Record(result.ArgumentList.Arguments[0].Expression, false);
        }
        int index = -1;
        bool label = false;
        if (owner == "ImGui") {
            if (labels.Contains(name) || name == "Combo" || name.StartsWith("DragFloat") || name.StartsWith("SliderFloat") || name.StartsWith("InputInt")) { index = 0; label = true; }
            else if (name == "LabelText") { index = 0; label = true; }
            else if (texts.Contains(name)) index = 0;
            else if (name == "TextColored") index = 1;
        } else if (owner == "AppImguiHelpers") {
            if (name is "InputFilepath" or "InputFolder" or "HotkeyMenuItem") { index = 0; label = true; }
            else if (name == "ClearableInputText") index = 1;
        } else if (owner == "ImguiHelpers") {
            if (helperLabels.Contains(name)) { index = 0; label = true; }
            else if (helperTexts.Contains(name)) index = 0;
            else if (name == "TextColoredWrapped") index = 1;
        }
        if (owner == "ResourcePathPicker" && name == "Show") { index = 0; label = true; }
        if (owner == "QuaternionFieldHandler" && name == "HandleQuaternion") { index = 0; label = true; }
        if (name is "ShowTooltip" or "ShowError" or "ShowMessage") index = 0;
        if (name is "CopyToClipboard" or "ShowToast") index = 1;
        if (index >= 0 && result.ArgumentList.Arguments.Count > index) {
            var arg = result.ArgumentList.Arguments[index];
            result = result.ReplaceNode(arg, arg.WithExpression(Wrap(arg.Expression, label)));
        }
        // A hint is display text, not a control ID.
        if (owner == "ImGui" && name == "InputTextWithHint" && result.ArgumentList.Arguments.Count > 1) {
            var arg = result.ArgumentList.Arguments[1];
            result = result.ReplaceNode(arg, arg.WithExpression(Wrap(arg.Expression, false)));
        }
        return result;
    }

    public override SyntaxNode? VisitObjectCreationExpression(ObjectCreationExpressionSyntax node)
    {
        var result = (ObjectCreationExpressionSyntax)base.VisitObjectCreationExpression(node)!;
        var name = node.Type.ToString();
        if (result.ArgumentList == null) return result;
        if (name is "FileFilter" or "FixedString" or "IconString" or "TextTooltip") {
            foreach (var arg in result.ArgumentList.Arguments.Take(name == "TextTooltip" ? 2 : 1)) Record(arg.Expression, false);
        }
        if (name is "ErrorModal" or "ConfirmationDialog" or "NameInputDialog") {
            for (int i = 0; i < Math.Min(2, result.ArgumentList!.Arguments.Count); i++) {
                var arg = result.ArgumentList!.Arguments[i];
                result = result.ReplaceNode(arg, arg.WithExpression(Wrap(arg.Expression, false)));
            }
        }
        return result;
    }

    public override SyntaxNode? VisitVariableDeclarator(VariableDeclaratorSyntax node)
    {
        var name = node.Identifier.ValueText;
        if (node.Initializer != null && Regex.IsMatch(name, "(?i)(labels|names|tips|options|WindowName|title|helptext|message|description|tooltip|text)$")) {
            foreach (var literal in node.Initializer.DescendantNodes().OfType<LiteralExpressionSyntax>()) Record(literal, false);
        }
        var result = (VariableDeclaratorSyntax)base.VisitVariableDeclarator(node)!;
        if (result.Initializer != null && node.Ancestors().OfType<LocalDeclarationStatementSyntax>().FirstOrDefault() is { } local
            && !local.Modifiers.Any(SyntaxKind.ConstKeyword)
            && Regex.IsMatch(name, "(?i)^(text|helptext|.*label.*|.*tooltip.*|message|description|.*Text|title|note)$")) {
            result = result.WithInitializer(result.Initializer.WithValue(Wrap(result.Initializer.Value, false)));
        }
        return result;
    }

    public override SyntaxNode? VisitArgument(ArgumentSyntax node)
    {
        if (node.NameColon?.Name.Identifier.ValueText is "label" or "title" or "tooltip" or "description") Record(node.Expression, false);
        return base.VisitArgument(node);
    }

    public override SyntaxNode? VisitEnumDeclaration(EnumDeclarationSyntax node)
    {
        if (file.Contains("Imgui") || file.Contains("Configuration")) {
            foreach (var member in node.Members) {
                var key = member.Identifier.ValueText;
                if (!entries.TryGetValue(key, out var locations)) entries[key] = locations = new();
                locations.Add(file + ":" + (member.GetLocation().GetLineSpan().StartLinePosition.Line + 1));
            }
        }
        return base.VisitEnumDeclaration(node);
    }

    public override SyntaxNode? VisitPropertyDeclaration(PropertyDeclarationSyntax node)
    {
        if (node.Identifier.ValueText is "HandlerName" or "Title" or "Name") {
            if (node.ExpressionBody != null) Record(node.ExpressionBody.Expression, false);
        }
        return base.VisitPropertyDeclaration(node);
    }

    private ExpressionSyntax Wrap(ExpressionSyntax expression, bool label)
    {
        if (expression is ConditionalExpressionSyntax conditional) {
            return conditional.WithWhenTrue(Wrap(conditional.WhenTrue, label)).WithWhenFalse(Wrap(conditional.WhenFalse, label));
        }
        if (expression is BinaryExpressionSyntax binary && binary.IsKind(SyntaxKind.AddExpression)) {
            // Translate only static words; never pass dynamic file/object names through a dictionary.
            return binary.WithLeft(Wrap(binary.Left, false)).WithRight(Wrap(binary.Right, false));
        }
        if (!Record(expression, label)) return expression;
        var isUtf8 = expression.IsKind(SyntaxKind.Utf8StringLiteralExpression);
        var method = expression is InterpolatedStringExpressionSyntax ? (label ? "FormatLabel" : "F") : isUtf8 ? (label ? "LabelUtf8" : "Utf8") : (label ? "Label" : "T");
        if (isUtf8) expression = SyntaxFactory.LiteralExpression(SyntaxKind.StringLiteralExpression, SyntaxFactory.Literal(((LiteralExpressionSyntax)expression).Token.ValueText)).WithTriviaFrom(expression);
        return SyntaxFactory.ParseExpression($"UiText.{method}({expression.WithoutTrivia()})").WithTriviaFrom(expression);
    }

    private bool Record(ExpressionSyntax expression, bool label)
    {
        string? template = null;
        if (expression is LiteralExpressionSyntax literal && (literal.IsKind(SyntaxKind.StringLiteralExpression) || literal.IsKind(SyntaxKind.Utf8StringLiteralExpression))) template = literal.Token.ValueText;
        if (expression is InterpolatedStringExpressionSyntax interpolated) {
            var index = 0;
            template = string.Concat(interpolated.Contents.Select(part => part is InterpolatedStringTextSyntax text
                ? text.TextToken.ValueText
                : "{" + index++ + ((InterpolationSyntax)part).AlignmentClause?.ToString() + ((InterpolationSyntax)part).FormatClause?.ToString() + "}"));
        }
        if (template == null || template.StartsWith("##") || !Regex.IsMatch(template, "[A-Za-z]{2}")) return false;
        if (label && template.Contains("##")) template = template[..template.IndexOf("##", StringComparison.Ordinal)];
        if (!entries.TryGetValue(template, out var locations)) entries[template] = locations = new();
        locations.Add(file + ":" + (expression.GetLocation().GetLineSpan().StartLinePosition.Line + 1));
        return true;
    }
}
