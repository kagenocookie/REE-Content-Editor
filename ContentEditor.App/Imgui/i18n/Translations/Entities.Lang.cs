using System.Text;
using ContentEditor.Core;
using ReeLib;

namespace ContentEditor.App;

public static partial class Lang
{
    public static class Entities
    {
        public static readonly FixedString Entity = "Entity";
        public static readonly FixedString Template = "Template";
        public static readonly FixedString EntityCreateNoTemplates = "No templates yet defined for this entity type. Duplicate or create a new template from an existing one first.";
        public static readonly FixedString EntityCreateBlankDisallowed = "Creating blank instances of this entity type is not supported. Duplicate an existing one or select a template.";
        public static readonly FixedString ChangeLabel = "Change label";
        public static readonly FixedString NewLabel = "New label";
        public static readonly FixedString CancelRename = "Cancel Rename";
        public static readonly FixedString ConfirmRename = "Confirm Rename";
        public static readonly FixedString OpenInNewWindow = "Open entity in separate window";
        public static readonly FixedString EntityNotFound = "Selected entity could not be found";
        public static readonly FixedString ShowActiveBundleOnly = "Show only active bundle entities";
    }
}
