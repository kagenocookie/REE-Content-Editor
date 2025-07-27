using System.Collections;
using System.Reflection;
using System.Text.RegularExpressions;
using ContentEditor.App.ImguiHandling;
using ContentEditor.Editor;
using ContentPatcher;
using ReeLib;

namespace ContentEditor.App;

public class OpenFileContext
{
    public REFileFormat format;
    public string? gamePath;
    public string? diskFilepath;

    public OpenFileContext(string filepath, bool isLocalFile)
    {
        format = PathUtils.ParseFileFormat(filepath);
        if (isLocalFile) {
            diskFilepath = filepath;
        } else {
            gamePath = filepath;
        }
    }
}

public partial class WindowHandlerFactory
{
    private static Dictionary<Type, Func<CustomField, IObjectUIHandler>>? customFieldImguiHandlers;

    private static HashSet<string> NonEnumIntegerTypes = [
        "System.Int8", "System.UInt8",
        "System.Int16", "System.UInt16",
        "System.Int32", "System.UInt32",
        "System.Int64", "System.UInt64",
    ];

    private static readonly Dictionary<RszClass, StringFormatter> classFormatters = new();

    private static bool showPrettyLabels = true;
    private static readonly Dictionary<string, string> prettyLabels = new();

    static WindowHandlerFactory()
    {
        AppConfig.Instance.PrettyFieldLabels.ValueChanged += (bool newValue) => showPrettyLabels = newValue;
        showPrettyLabels = AppConfig.Instance.PrettyFieldLabels.Get();
    }

    public static void SetClassFormatter(RszClass cls, StringFormatter formatter)
    {
        classFormatters[cls] = formatter;
    }

    public static IWindowHandler? CreateFileResourceHandler(ContentWorkspace env, FileHandle file)
    {
        switch (file.Format.format) {
            case KnownFileFormats.UserData:
                return new UserDataFileEditor(env, file);
            case KnownFileFormats.Message:
                return new MsgFileEditor(env, file);
            case KnownFileFormats.UVSequence:
                return new UVSequenceFileEditor(env, file);
        }
        return null;
    }

    public static IObjectUIHandler CreateRSZFieldHandler(UIContext context, RszField field)
    {
        if (field.array) {
            return context.uiHandler = new ArrayRSZHandler(field);
        }
        return CreateRSZFieldElementHandler(context, field);
    }

    public static IObjectUIHandler CreateRSZFieldElementHandler(UIContext context, RszField field)
    {
        var handler = CreateRSZFieldElementHandlerRaw(context, field, out var fieldCfg, out var patch);
        if (fieldCfg?.ReadOnly == true) {
            context.uiHandler = new ReadOnlyWrapperHandler(handler);
        }
        return handler;
    }

    public static IObjectUIHandler CreateRSZFieldElementHandlerRaw(UIContext context, RszField field, out FieldConfig? fieldConfig, out ClassConfig? patchConfig)
    {
        static IObjectUIHandler? TryCreateEnumHandler(ContentWorkspace? workspace, string fieldClassname)
        {
            if (!string.IsNullOrEmpty(fieldClassname) && workspace != null && !NonEnumIntegerTypes.Contains(fieldClassname)) {
                var enumdesc = workspace.Env.TypeCache.GetEnumDescriptor(fieldClassname);
                if (enumdesc.IsFlags) {
                    return new FlagsEnumFieldHandler(workspace.Env.TypeCache.GetEnumDescriptor(fieldClassname));
                } else {
                    return new EnumFieldHandler(workspace.Env.TypeCache.GetEnumDescriptor(fieldClassname));
                }
            }
            return null;
        }
        fieldConfig = null;
        patchConfig = null;

        var ws = context.GetWorkspace();
        if (ws != null) {
            if (context.parent?.target is RszInstance parent) {
                fieldConfig = ws.Config.Get(parent.RszClass.name, field.name);
                if (fieldConfig != null) {
                    if (fieldConfig.Enum != null) {
                        context.uiHandler = TryCreateEnumHandler(ws, fieldConfig.Enum);
                        if (context.uiHandler != null) return context.uiHandler;
                    }
                }
            }

            patchConfig = ws.Config.Get(field.original_type);
            if (patchConfig != null) {
                // TODO
            }
        }

        return context.uiHandler = field.type switch {
            RszFieldType.Object or RszFieldType.Struct => new NestedRszInstanceHandler(),
            RszFieldType.String => new StringFieldHandler(),
            RszFieldType.RuntimeType => new StringFieldHandler(), // TODO proper RuntimeType editor (use il2cpp data)
            RszFieldType.Resource => new ResourcePathPicker(ws, field),

            RszFieldType.UserData => new UserDataReferenceHandler(field),

            RszFieldType.S8 => new NumericFieldHandler<sbyte>(ImGuiNET.ImGuiDataType.S8),
            RszFieldType.S16 => TryCreateEnumHandler(ws, field.original_type) ?? new NumericFieldHandler<short>(ImGuiNET.ImGuiDataType.S16),
            RszFieldType.S32 => TryCreateEnumHandler(ws, field.original_type) ?? new NumericFieldHandler<int>(ImGuiNET.ImGuiDataType.S32),
            RszFieldType.S64 => TryCreateEnumHandler(ws, field.original_type) ?? new NumericFieldHandler<long>(ImGuiNET.ImGuiDataType.S64),
            RszFieldType.U8 or RszFieldType.UByte => new NumericFieldHandler<byte>(ImGuiNET.ImGuiDataType.U8),
            RszFieldType.U16 => TryCreateEnumHandler(ws, field.original_type) ?? new NumericFieldHandler<ushort>(ImGuiNET.ImGuiDataType.U16),
            RszFieldType.U32 => TryCreateEnumHandler(ws, field.original_type) ?? new NumericFieldHandler<uint>(ImGuiNET.ImGuiDataType.U32),
            RszFieldType.U64 => TryCreateEnumHandler(ws, field.original_type) ?? new NumericFieldHandler<ulong>(ImGuiNET.ImGuiDataType.U64),
            RszFieldType.F32 => new NumericFieldHandler<float>(ImGuiNET.ImGuiDataType.Float),
            RszFieldType.F64 => new NumericFieldHandler<double>(ImGuiNET.ImGuiDataType.Double),
            RszFieldType.Bool => new BoolFieldHandler(),

            RszFieldType.Vec2 or ReeLib.RszFieldType.Float2 or RszFieldType.Point => new Vector2FieldHandler(),
            RszFieldType.Vec3 or RszFieldType.Float3 => new Vector3FieldHandler(),
            RszFieldType.Vec4 or RszFieldType.Float4 => new Vector4FieldHandler(),
            RszFieldType.Quaternion => new QuaternionFieldHandler(),
            RszFieldType.Position => new PositionFieldHandler(),
            RszFieldType.Range => new RangeFieldHandler(),
            RszFieldType.RangeI => new IntRangeFieldHandler(),
            RszFieldType.Size => new SizeFieldHandler(),
            RszFieldType.Color => new ColorFieldHandler(),
            RszFieldType.Rect => new RectFieldHandler(),

            // RszFieldType.Rect3D => variant.As<Rect3D>().ToRsz(),
            // RszFieldType.Mat3 => new ColorFieldHandler(),
            // RszFieldType.Mat4 => new ColorFieldHandler(),

            // RszFieldType.Uint2 => TODO
            // RszFieldType.Uint3 => TODO
            // RszFieldType.Uint4 => TODO
            // RszFieldType.Int2 => TODO
            // RszFieldType.Int3 => TODO
            // RszFieldType.Int4 => TODO

            RszFieldType.Guid => new GuidFieldHandler(),
            RszFieldType.Uri => new GuidFieldHandler(),

            // RszFieldType.OBB => variant.As<OrientedBoundingBox>().ToRsz(),
            // RszFieldType.AABB => (ReeLib.via.AABB)variant.AsAabb().ToRsz(),
            // RszFieldType.Sphere => variant.AsVector4().ToSphere(),
            // RszFieldType.Capsule => variant.As<Capsule>().ToRsz(),
            // RszFieldType.Area => variant.As<Area>().ToRsz(),
            // RszFieldType.TaperedCapsule => variant.As<TaperedCapsule>().ToRsz(),
            // RszFieldType.Cone => variant.As<Cone>().ToRsz(),
            // RszFieldType.Line => variant.As<Line>().ToRsz(),
            // RszFieldType.LineSegment => variant.As<LineSegment>().ToRsz(),
            // RszFieldType.Plane => variant.As<Plane>().ToRsz(),
            // RszFieldType.PlaneXZ => new ReeLib.via.PlaneXZ { dist = variant.AsSingle() },
            // RszFieldType.Ray => variant.As<Ray>().ToRsz(),
            // RszFieldType.RayY => variant.As<RayY>().ToRsz(),
            // RszFieldType.Segment => variant.As<Segment>().ToRsz(),
            // RszFieldType.Triangle => variant.As<Triangle>().ToRsz(),
            // RszFieldType.Cylinder => variant.As<Cylinder>().ToRsz(),
            // RszFieldType.Ellipsoid => variant.As<Ellipsoid>().ToRsz(),
            // RszFieldType.Torus => variant.As<Torus>().ToRsz(),
            // RszFieldType.Frustum => variant.As<Frustum>().ToRsz(),
            // RszFieldType.KeyFrame => variant.As<KeyFrame>().ToRsz(),
            // RszFieldType.Sfix => new ReeLib.via.sfix() { v = variant.AsInt32() },
            // RszFieldType.Sfix2 => variant.AsVector2I().ToSfix(),
            // RszFieldType.Sfix3 => variant.AsVector3I().ToSfix(),
            // RszFieldType.Sfix4 => variant.AsVector4I().ToSfix(),

            // RszFieldType.Data => variant.AsVector4I().ToSfix(),

            _ => new UnsupportedHandler(field)
        };
    }

    public static UIContext CreateRSZInstanceHandlerContext(UIContext context)
    {
        SetupRSZInstanceHandler(context);
        context.uiHandler = new RszInstanceHandler();
        return context;
    }

    public static UIContext CreateResourceEntityHandler(UIContext context)
    {
        var entity = context.Get<ResourceEntity>();
        context.uiHandler = new ContentEditorEntityImguiHandler();
        foreach (var field in entity.Config.DisplayFieldsOrder) {
            if (field.Condition?.IsEnabled(entity) == false) {
                continue;
            }

            var handler = GetCustomFieldImguiHandler(entity, field);
            if (handler != null) {
                var child = context.AddChild(field.label ?? field.name, entity, getter: (ctx) => ((ResourceEntity)ctx.target!).Get(field.name), setter: (ctx, val) => ((ResourceEntity)ctx.target!).Set(field.name, val as IContentResource));
                child.uiHandler = handler;
            }
        }
        return context;
    }

    private static IObjectUIHandler? GetCustomFieldImguiHandler(ResourceEntity entity, CustomField field)
    {
        if (customFieldImguiHandlers == null) {
            customFieldImguiHandlers = new();
            foreach (var type in typeof(ContentEditorRszInstanceHandler).Assembly.GetTypes()) {
                if (type.IsAbstract || !typeof(IObjectUIHandler).IsAssignableFrom(type)) continue;

                var attr = type.GetCustomAttribute<CustomFieldHandlerAttribute>();
                if (attr == null) continue;

                var method = type.GetInterfaceMap(typeof(IObjectUIInstantiator)).TargetMethods.First();
                if (method == null) {
                    throw new Exception($"Invalid ObjectHandler type {type} - must implement {typeof(IObjectUIInstantiator)} for UI display");
                }

                customFieldImguiHandlers[attr.HandledFieldType] = (Func<CustomField, IObjectUIHandler>)method.Invoke(null, Array.Empty<object?>())!;
            }
        }

        if (customFieldImguiHandlers.TryGetValue(field.GetType(), out var handler)) {
            return handler.Invoke(field);
        }

        return null;
    }

    public static StringFormatter? GetStringFormatter(RszInstance instance)
    {
        return classFormatters.GetValueOrDefault(instance.RszClass);
    }

    public static void SetupRSZInstanceHandler(UIContext context)
    {
        var instance = context.Get<RszInstance>();
        var ws = context.GetWorkspace();
        var config = ws?.Config.Get(instance.RszClass.name);
        // if (config?.FieldOrder != null) {
        //     // TODO support and use custom rsz field order
        // }
        if (classFormatters.TryGetValue(instance.RszClass, out var fmt)) {
            context.stringFormatter = fmt;
        }
        for (int i = 0; i < instance.RszClass.fields.Length; i++) {
            var field = instance.RszClass.fields[i];
            var fieldCtx = CreateRSZFieldContext(instance, i, field, context);
            context.children.Add(fieldCtx);
            CreateRSZFieldHandler(fieldCtx, field);
        }
    }

    public static UIContext CreateRSZArrayElementContext(UIContext parent, int index)
    {
        return new UIContext(index.ToString(), parent.GetRaw()!, parent.root, (ctx) => ((IList)ctx.target!)[index], (ctx, v) => ((IList)ctx.target!)[index] = v, new UIOptions())
        {
            parent = parent,
        };
    }

    public static UIContext CreateRSZFieldContext(RszInstance parent, string fieldName, UIContext parentContext)
    {
        var fieldIndex = parent.RszClass.IndexOfField(fieldName);
        var field = parent.RszClass.fields[fieldIndex];
        return CreateRSZFieldContext(parent, fieldIndex, field, parentContext);
    }

    public static UIContext CreateRSZFieldContext(RszInstance parent, int fieldIndex, RszField field, UIContext parentContext)
    {
        return new UIContext(GetFieldLabel(field), parent, parentContext.root, (ctx) => ((RszInstance)ctx.target!).Values[fieldIndex], (ctx, v) => ((RszInstance)ctx.target!).Values[fieldIndex] = v!, new UIOptions()) {
            parent = parentContext
        };
    }

    [GeneratedRegex(@"(\P{Ll})(\P{Ll}\p{Ll})")]
    private static partial Regex PascalCaseFixerRegex1();

    [GeneratedRegex(@"(\p{Ll})(\P{Ll})")]
    private static partial Regex PascalCaseFixerRegex2();

    private static string GetFieldLabel(RszField field)
    {
        if (!showPrettyLabels) return field.name;

        if (prettyLabels.TryGetValue(field.name, out var label)) {
            return label;
        }

        // https://stackoverflow.com/a/5796793/4721768
        label = field.name.TrimStart('_');
        label = PascalCaseFixerRegex1().Replace(label, "$1 $2");
        label = PascalCaseFixerRegex2().Replace(label, "$1 $2");
        prettyLabels[field.name] = label;
        return label;
    }
}
