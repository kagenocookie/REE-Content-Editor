using System.Collections;
using System.Collections.ObjectModel;
using System.Numerics;
using System.Reflection;
using System.Text.RegularExpressions;
using ContentEditor.App.ImguiHandling;
using ContentEditor.App.ImguiHandling.Mdf2;
using ContentEditor.Editor;
using ContentPatcher;
using ReeLib;
using ReeLib.Efx;
using ReeLib.Motbank;

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

public static partial class WindowHandlerFactory
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
    private static readonly HashSet<Type> reflectionIgnoredTypes = [typeof(FileHandler), typeof(RszFileOption), typeof(RszParser), typeof(RszClass)];
    private static readonly HashSet<string> ignoredProperties = ["set_Item", "get_Item"];
    private static readonly HashSet<(Type type, string field)> ignoredFields = [
        (typeof(BaseModel), nameof(BaseModel.Size)),
        (typeof(BaseModel), nameof(BaseModel.Start)),
        (typeof(BaseFile), nameof(BaseFile.Size)),
        (typeof(BaseFile), nameof(BaseFile.Embedded)),
        (typeof(MotlistItem), nameof(MotlistItem.Version)),
        (typeof(EFXAttribute), nameof(EFXAttribute.Version)),
        (typeof(EFXAttribute), nameof(EFXAttribute.type)),
        (typeof(EFXAttribute), nameof(EFXAttribute.IsTypeAttribute))
    ];
    private static HashSet<Type> genericListTypes = [typeof(List<>), typeof(ObservableCollection<>)];
    private static readonly Dictionary<Type, MemberInfo[]> typeMembers = new();

    private static readonly Dictionary<Type, Func<IObjectUIHandler>> csharpTypeHandlers = new()
    {
        { typeof(sbyte), () => new NumericFieldHandler<sbyte>(ImGuiNET.ImGuiDataType.S8) },
        { typeof(byte), () => new NumericFieldHandler<byte>(ImGuiNET.ImGuiDataType.U8) },
        { typeof(short), () => new NumericFieldHandler<short>(ImGuiNET.ImGuiDataType.S16) },
        { typeof(ushort), () => new NumericFieldHandler<ushort>(ImGuiNET.ImGuiDataType.U16) },
        { typeof(int), () => new NumericFieldHandler<int>(ImGuiNET.ImGuiDataType.S32) },
        { typeof(uint), () => new NumericFieldHandler<uint>(ImGuiNET.ImGuiDataType.U32) },
        { typeof(long), () => new NumericFieldHandler<long>(ImGuiNET.ImGuiDataType.S64) },
        { typeof(ulong), () => new NumericFieldHandler<ulong>(ImGuiNET.ImGuiDataType.U64) },
        { typeof(float), () => new NumericFieldHandler<float>(ImGuiNET.ImGuiDataType.Float) },
        { typeof(double), () => new NumericFieldHandler<double>(ImGuiNET.ImGuiDataType.Double) },
        { typeof(byte[]), () => new ByteArrayHandler() },
        { typeof(string), () => StringFieldHandler.Instance },
        { typeof(Guid), () => GuidFieldHandler.Instance },
        { typeof(bool), () => BoolFieldHandler.Instance },
        { typeof(Vector2), () => Vector2FieldHandler.Instance },
        { typeof(Vector3), () => Vector3FieldHandler.Instance },
        { typeof(Vector4), () => Vector4FieldHandler.Instance },
        { typeof(ReeLib.via.Position), () => PositionFieldHandler.Instance },
        { typeof(ReeLib.via.Range), () => RangeFieldHandler.Instance },
        { typeof(ReeLib.via.RangeI), () => IntRangeFieldHandler.Instance },
        { typeof(ReeLib.via.Size), () => SizeFieldHandler.Instance },
        { typeof(ReeLib.via.Color), () => ColorFieldHandler.Instance },
        { typeof(ReeLib.via.Rect), () => RectFieldHandler.Instance },
        { typeof(Quaternion), () => QuaternionFieldHandler.Instance },
        { typeof(RszInstance), () => new NestedRszInstanceHandler() },
    };

    private static readonly HashSet<GameIdentifier> setupGames = new();
    private static readonly Dictionary<RszClass, List<Func<UIContext, bool>>> classActions = new();
    private static readonly Dictionary<RszClass, Func<IObjectUIHandler>> classHandlers = new();

    static WindowHandlerFactory()
    {
        AppConfig.Instance.PrettyFieldLabels.ValueChanged += (bool newValue) => showPrettyLabels = newValue;
        showPrettyLabels = AppConfig.Instance.PrettyFieldLabels.Get();

        var types = typeof(WindowHandlerFactory).Assembly.GetTypes();
        var subtypes = types.Concat(typeof(EFXAttribute).Assembly.GetTypes());
        var dict = new Dictionary<Type, (int priority, Func<IObjectUIHandler> fact)>();
        foreach (var type in types) {
            var handlers = type.GetCustomAttributes<ObjectImguiHandlerAttribute>();
            if (handlers.Any()) {
                if (!type.IsAssignableTo(typeof(IObjectUIHandler))) {
                    Console.Error.WriteLine($"Invalid [WindowHandlerFactory] type {type}");
                    continue;
                }

                foreach (var attr in handlers) {
                    Func<IObjectUIHandler> fact;
                    if (attr.Stateless) {
                        var inst = (IObjectUIHandler)Activator.CreateInstance(type)!;
                        fact = () => inst;
                    } else {
                        fact = () => (IObjectUIHandler)Activator.CreateInstance(type)!;
                    }
                    csharpTypeHandlers[attr.HandledFieldType] = fact;
                    if (!dict.TryGetValue(attr.HandledFieldType, out var entry) || attr.Priority < entry.priority) {
                        dict[attr.HandledFieldType] = (attr.Priority, fact);
                    }

                    if (attr.Inherited || attr.HandledFieldType.IsInterface) {
                        foreach (var subtype in subtypes.Where(t => !t.IsAbstract && t.IsAssignableTo(attr.HandledFieldType))) {
                            if (!dict.TryGetValue(subtype, out entry) || attr.Priority < entry.priority) {
                                dict[subtype] = (attr.Priority, fact);
                            }
                        }
                    }
                }
            }
        }

        foreach (var (type, data) in dict) {
            csharpTypeHandlers.TryAdd(type, data.fact);
        }
    }

    public static void SetupTypesForGame(GameIdentifier game, Workspace env)
    {
        if (!setupGames.Add(game)) return;

        var types = typeof(WindowHandlerFactory).Assembly.GetTypes();
        foreach (var type in types) {
            if (type.GetCustomAttribute<RszContextActionAttribute>() != null) {
                foreach (var method in type.GetMethods(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)) {
                    var actions = method.GetCustomAttributes<RszContextActionAttribute>();
                    foreach (var attr in actions) {
                        if (attr.Games.Length > 0 && !attr.Games.Contains(game.name)) continue;

                        var cls = env.RszParser.GetRSZClass(attr.Classname);
                        if (cls == null) {
                            Logger.Debug($"Class {attr.Classname} not found for game {game}");
                            continue;
                        }

                        if (method.ReturnType != typeof(bool) || method.GetParameters().Length != 1 || method.GetParameters()[0].ParameterType != typeof(UIContext)) {
                            Logger.Error($"Method {type.FullName}.{method.Name}() is not valid for {nameof(RszContextActionAttribute)}. Should be `bool {method.Name}(UIContext context)`.");
                            continue;
                        }

                        if (!classActions.TryGetValue(cls, out var list)) {
                            classActions[cls] = list = new();
                        }

                        list.Add((ctx) => (bool)method.Invoke(null, [ctx])!);
                    }
                }
            }

            if (type.GetCustomAttribute<RszClassHandlerAttribute>() != null) {
                if (!type.IsAssignableTo(typeof(IObjectUIHandler))) {
                    Logger.Error($"RszClassHandler annotated class must implement {nameof(IObjectUIHandler)} (type {type.FullName})");
                    continue;
                }

                foreach (var attr in type.GetCustomAttributes<RszClassHandlerAttribute>()) {
                    if (attr.Games.Length > 0 && !attr.Games.Contains(game.name)) continue;

                    var cls = env.RszParser.GetRSZClass(attr.Classname);
                    if (cls == null) {
                        Logger.Debug($"Class {attr.Classname} not found for game {game}");
                        continue;
                    }

                    classHandlers[cls] = () => (IObjectUIHandler)Activator.CreateInstance(type)!;
                }
            }
        }
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
            case KnownFileFormats.UserVariables:
                return new UvarEditor(env, file);
            case KnownFileFormats.MaterialDefinition:
                return new MdfEditor(env, file);
            case KnownFileFormats.Prefab:
                return new PrefabEditor(env, file);
            case KnownFileFormats.Scene:
                return new SceneEditor(env, file);
            case KnownFileFormats.Effect:
                return new ImguiHandling.Efx.EfxEditor(env, file);
            case KnownFileFormats.RequestSetCollider:
                return new ImguiHandling.Rcol.RcolEditor(env, file);
            case KnownFileFormats.MotionList:
                return new MotlistEditor(env, file);
            case KnownFileFormats.CollisionDefinition:
                return new RawDataEditor<CdefFile>(env, file);
            case KnownFileFormats.DynamicsDefinition:
                return new RawDataEditor<DefFile>(env, file);
            case KnownFileFormats.CollisionFilter:
                return new CfilEditor(env, file);
            case KnownFileFormats.CollisionMesh:
                return new McolEditor(env, file);
            case KnownFileFormats.Mesh:
                return new MeshViewer(env, file);
        }

        if (TextureViewer.IsSupportedFileExtension(file.Filepath)) {
            return new TextureViewer(file);
        }

        if (MeshViewer.IsSupportedFileExtension(file.Filepath)) {
            return new MeshViewer(env, file);
        }

        if (file.Resource is not DummyFileResource) {
            return new RawDataEditor(env, file);
        }

        if (file.Filepath.EndsWith(".json") || file.Filepath.EndsWith(".txt") || file.Filepath.EndsWith(".ini")) {
            return new TextViewer(file.Stream, $"File ({file.HandleType} {file.FileSource})\n{file.Filepath}", true);
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

    #region Reflection-based handlers
    public static bool SetupObjectUIContext(UIContext context, Type? type, bool includePrivate = false, MemberInfo[]? members = null)
    {
        var instance = context.GetRaw();
        var ws = context.GetWorkspace();
        var targetType = instance?.GetType() ?? type;
        if (targetType == null || instance == null) {
            context.uiHandler = new UnsupportedHandler(targetType);
            return false;
        }

        var reflectionOptions = BindingFlags.Instance | BindingFlags.Public;
        if (includePrivate) reflectionOptions |= BindingFlags.NonPublic;
        var isValueType = targetType.IsValueType;

        if (members == null && !typeMembers.TryGetValue(targetType, out members)) {
            var list = new List<MemberInfo>();

            foreach (var field in targetType.GetFields(reflectionOptions)) {
                if (reflectionIgnoredTypes.Contains(field.FieldType) || ignoredFields.Contains((field.DeclaringType ?? targetType, field.Name))) continue;
                if (field.Name.EndsWith(">k__BackingField")) continue;

                list.Add(field);
            }

            foreach (var prop in targetType.GetProperties(reflectionOptions)) {
                if (prop.IsSpecialName ||
                    prop.GetMethod == null ||
                    reflectionIgnoredTypes.Contains(prop.PropertyType) ||
                    ignoredProperties.Contains(prop.GetMethod.Name) ||
                    ignoredFields.Contains((prop.DeclaringType ?? targetType, prop.Name))
                ) {
                    continue;
                }

                list.Add(prop);
            }
            typeMembers[targetType] = members = list.ToArray();
        }

        foreach (var field in members) {
            var ctx = context.AddChild(
                GetFieldLabel(field.Name),
                    instance,
                    getter: field switch {
                        FieldInfo fi => (c) => fi.GetValue(c.target),
                        PropertyInfo prop => (c) => prop.GetValue(c.target),
                        _ => throw new Exception()
                    },
                    setter: field switch {
                        FieldInfo fi => !isValueType ? (c, v) => fi.SetValue(c.target, v) : (c, v) => {
                            fi.SetValue(c.target, v);
                            c.parent?.Set(c.target);
                        },
                        PropertyInfo prop => prop.SetMethod == null ? null : (c, v) => prop.SetValue(c.target, v),
                        _ => throw new Exception()
                    }
                );

            ctx.uiHandler = CreateReflectionFieldHandler(context, field);
            if (field is PropertyInfo pp && pp.SetMethod == null && pp.PropertyType.IsValueType) {
                ctx.uiHandler = new ReadOnlyWrapperHandler(ctx.uiHandler);
            }
        }

        return true;
    }

    public static IObjectUIHandler CreateReflectionFieldHandler(UIContext context, MemberInfo field)
    {
        var fieldType = ((field as FieldInfo)?.FieldType) ?? (field as PropertyInfo)?.PropertyType;
        if (fieldType == null) return new UnsupportedHandler();
        if (fieldType.IsClass && fieldType != typeof(string)) {
            var target = context.GetRaw();
            var value = (field as FieldInfo)?.GetValue(target) ?? (field as PropertyInfo)?.GetValue(target);
            if (value != null) {
                return CreateUIHandler(value, value.GetType());
            } else {
                return new LazyPlainObjectHandler(fieldType);
            }
        }
        return CreateUIHandler(context.GetRaw(), fieldType);
    }

    public static void AddDefaultHandler(this UIContext context)
    {
        context.uiHandler = CreateUIHandler(context.GetRaw(), context.GetRaw()?.GetType());
    }

    public static void AddDefaultHandler<T>(this UIContext context)
    {
        context.uiHandler = CreateUIHandler(context.GetRaw(), typeof(T));
    }

    public static IObjectUIHandler CreateUIHandler<T>(object? value)
    {
        return CreateUIHandler(value, typeof(T));
    }

    public static IObjectUIHandler CreateUIHandler(object? value, Type? valueType)
    {
        valueType ??= value?.GetType();
        if (valueType == null) {
            return new UnsupportedHandler();
        }

        if (csharpTypeHandlers.TryGetValue(valueType, out var fac)) {
            return fac.Invoke();
        }

        if (valueType.IsArray) {
            return new ArrayHandler(valueType.GetElementType()!);
        }

        if (valueType.IsEnum) {
            if (valueType.GetCustomAttribute<FlagsAttribute>() != null) {
                return (IObjectUIHandler)Activator.CreateInstance(typeof(CsharpFlagsEnumFieldHandler<,>).MakeGenericType(valueType, valueType.GetEnumUnderlyingType()))!;
            } else {
                return new CsharpEnumHandler(valueType);
            }
        }

        if (valueType.IsGenericType && genericListTypes.Contains(valueType.GetGenericTypeDefinition())) {
            return new ListHandler(valueType.GetGenericArguments()[0]) { CanCreateNewElements = true };
        }

        if (valueType.IsClass) {
            return new LazyPlainObjectHandler(valueType);
        }
        if (valueType.IsValueType) {
            return new LazyPlainObjectHandler(valueType);
        }
        if (valueType.IsAbstract) {
            if (value != null && value.GetType() != valueType) {
                return CreateUIHandler(value, value.GetType());
            }
        }

        return new UnsupportedHandler(valueType);
    }

    public static void SetupArrayElementHandler(UIContext context, Type elementType)
    {
        if (csharpTypeHandlers.TryGetValue(elementType, out var fac)) {
            context.uiHandler = fac.Invoke();
            return;
        }

        if (elementType.IsArray) {
            context.uiHandler = new UnsupportedHandler(elementType.MakeArrayType());
            return;
        }

        if (elementType.IsEnum) {
            context.uiHandler = new CsharpEnumHandler(elementType);
            return;
        }

        if (elementType.IsClass || elementType.IsValueType) {
            context.uiHandler = new LazyPlainObjectHandler(elementType);
            // WindowHandlerFactory.SetupObjectUIContext(context, elementType);
            return;
        }

        context.uiHandler = new UnsupportedHandler(elementType);
    }
    #endregion

    #region RSZ based handlers
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

        var originalClass = string.IsNullOrEmpty(field.original_type) ? null : ws?.Env.RszParser.GetRSZClass(field.original_type);
        if (originalClass != null && classHandlers.TryGetValue(originalClass, out var handlerFunc)) {
            return context.uiHandler = handlerFunc.Invoke();
        }

        static IObjectUIHandler CreateObjectHandler(RszField field, ContentWorkspace? workspace)
        {
            if (string.IsNullOrEmpty(field.original_type) || workspace == null) return new NestedRszInstanceHandler();
            var subtypes = workspace.Env.TypeCache.GetSubclasses(field.original_type);
            if (subtypes.Count == 0 || subtypes.Count == 1 && subtypes[0] == field.original_type) return new NestedRszInstanceHandler();

            return new NestedUIHandlerStringSuffixed(new RszClassnamePickerHandler(field.original_type));
        }

        return context.uiHandler = field.type switch {
            RszFieldType.Object => CreateObjectHandler(field, ws),
            RszFieldType.Struct => new NestedRszInstanceHandler(),
            RszFieldType.String => StringFieldHandler.Instance,
            RszFieldType.RuntimeType => StringFieldHandler.Instance, // TODO proper RuntimeType editor (use il2cpp data)
            RszFieldType.Resource => new ResourcePathPicker(ws, field),

            RszFieldType.UserData => new UserDataReferenceHandler(),

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
            RszFieldType.Bool => BoolFieldHandler.Instance,

            RszFieldType.Vec2 or ReeLib.RszFieldType.Float2 or RszFieldType.Point => Vector2FieldHandler.Instance,
            RszFieldType.Vec3 or RszFieldType.Float3 => Vector3FieldHandler.Instance,
            RszFieldType.Vec4 or RszFieldType.Float4 => Vector4FieldHandler.Instance,
            RszFieldType.Quaternion => QuaternionFieldHandler.Instance,
            RszFieldType.Position => PositionFieldHandler.Instance,
            RszFieldType.Range => RangeFieldHandler.Instance,
            RszFieldType.RangeI => IntRangeFieldHandler.Instance,
            RszFieldType.Size => SizeFieldHandler.Instance,
            RszFieldType.Color => ColorFieldHandler.Instance,
            RszFieldType.Rect => RectFieldHandler.Instance,

            // RszFieldType.Rect3D => variant.As<Rect3D>().ToRsz(),
            // RszFieldType.Mat3 => new ColorFieldHandler(),
            // RszFieldType.Mat4 => new ColorFieldHandler(),

            RszFieldType.Uint2 => Uint2FieldHandler.Instance,
            RszFieldType.Uint3 => Uint3FieldHandler.Instance,
            RszFieldType.Uint4 => Uint4FieldHandler.Instance,
            RszFieldType.Int2 => Int2FieldHandler.Instance,
            RszFieldType.Int3 => Int3FieldHandler.Instance,
            RszFieldType.Int4 => Int4FieldHandler.Instance,

            RszFieldType.Guid => GuidFieldHandler.Instance,

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

            RszFieldType.Data => new ByteArrayHandler(),

            _ => RszInstance.RszFieldTypeToCSharpType(field.type) is Type otherType
                ? csharpTypeHandlers.GetValueOrDefault(otherType)?.Invoke() ?? new LazyPlainObjectHandler(otherType)
                : new UnsupportedHandler(field)
        };
    }

    public static void SetupRSZInstanceHandler(UIContext context)
    {
        var instance = context.Get<RszInstance>();
        if (instance == null) {
            context.uiHandler ??= RszInstanceHandler.Instance;
            return;
        }

        var ws = context.GetWorkspace();
        var config = ws?.Config.Get(instance.RszClass.name);
        // if (config?.FieldOrder != null) {
        //     // TODO support and use custom rsz field order
        // }
        if (classFormatters.TryGetValue(instance.RszClass, out var fmt)) {
            context.stringFormatter = fmt;
        }
        if (classHandlers.TryGetValue(instance.RszClass, out var handlerFact)) {
            context.uiHandler = handlerFact.Invoke();
            return;
        } else if (context.uiHandler == null) {
            context.uiHandler = RszInstanceHandler.Instance;
        }

        AddRszInstanceFieldChildren(instance, context, 0);
    }

    public static void AddRszInstanceFieldChildren(UIContext context, int startIndex = 0)
    {
        var instance = context.Get<RszInstance>();
        AddRszInstanceFieldChildren(instance, context, startIndex);
    }

    private static void AddRszInstanceFieldChildren(RszInstance instance, UIContext context, int startIndex = 0)
    {
        for (int i = startIndex; i < instance.RszClass.fields.Length; i++) {
            var field = instance.RszClass.fields[i];
            var fieldCtx = CreateRSZFieldContext(instance, i, field, context);
            context.children.Add(fieldCtx);
            CreateRSZFieldHandler(fieldCtx, field);
        }
    }

    public static UIContext CreateRSZFieldContext(RszInstance parent, string fieldName, UIContext parentContext)
    {
        var fieldIndex = parent.RszClass.IndexOfField(fieldName);
        var field = parent.RszClass.fields[fieldIndex];
        return CreateRSZFieldContext(parent, fieldIndex, field, parentContext);
    }

    public static UIContext CreateRSZFieldContext(RszInstance parent, int fieldIndex, RszField field, UIContext parentContext)
    {
        return new UIContext(GetFieldLabel(field.name), parent, parentContext.root, (ctx) => ((RszInstance)ctx.target!).Values[fieldIndex], (ctx, v) => ((RszInstance)ctx.target!).Values[fieldIndex] = v!, new UIOptions()) {
            parent = parentContext
        };
    }

    public static bool ShowCustomActions(UIContext context)
    {
        static bool Invoke(List<Func<UIContext, bool>> actions, UIContext context)
        {
            var end = false;
            foreach (var act in actions) {
                end = act.Invoke(context) || end;
            }
            return end;
        }

        if (context.TryCast<RszInstance>(out var instance)) {
            // single instance
            if (classActions.TryGetValue(instance.RszClass, out var actions)) {
                return Invoke(actions, context);
            }
        } else if (context.TryCast<List<object>>(out var list)) {
            // multi instance or arbitrary value array
            var ws = context.GetWorkspace();
            if (ws == null) return false;

            if (context.parent?.TryCast<RszInstance>(out var parent) == true) {
                var fieldIndex = Array.IndexOf(parent.Values, list);
                if (fieldIndex == -1) {
                    Logger.Debug("Invalid rsz array parent for custom actions");
                    return false;
                }

                if (classActions.TryGetValue(ws.Env.RszParser.GetRSZClass(parent.Fields[fieldIndex].original_type)!, out var actions)) {
                    return Invoke(actions, context);
                }
            } else if (list.FirstOrDefault() is RszInstance first) {
                if (classActions.TryGetValue(first.RszClass, out var actions)) {
                    return Invoke(actions, context);
                }
            }
        }

        return false;
    }
    #endregion

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
                var child = context.AddChild(field.label, entity, getter: (ctx) => ((ResourceEntity)ctx.target!).Get(field.name), setter: (ctx, val) => ((ResourceEntity)ctx.target!).Set(field.name, val as IContentResource));
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

    public static string GetString(this RszInstance instance)
    {
        return classFormatters.TryGetValue(instance.RszClass, out var fmt) ? fmt.GetString(instance) : instance.Name;
    }

    public static StringFormatter? GetStringFormatter(RszInstance instance)
    {
        return classFormatters.GetValueOrDefault(instance.RszClass);
    }

    public static UIContext CreateListElementContext(UIContext parent, int index)
    {
        return new UIContext(index.ToString(), parent.GetRaw()!, parent.root, (ctx) => ((IList)ctx.target!)[index], (ctx, v) => ((IList)ctx.target!)[index] = v, new UIOptions()) {
            parent = parent,
        };
    }

    [GeneratedRegex(@"(\P{Ll})(\P{Ll}\p{Ll})")]
    private static partial Regex PascalCaseFixerRegex1();

    [GeneratedRegex(@"(\p{Ll})(\P{Ll})")]
    private static partial Regex PascalCaseFixerRegex2();

    private static string GetFieldLabel(string name)
    {
        if (!showPrettyLabels) return name;

        if (prettyLabels.TryGetValue(name, out var label)) {
            return label;
        }

        // https://stackoverflow.com/a/5796793/4721768
        label = name.TrimStart('_');
        label = PascalCaseFixerRegex1().Replace(label, "$1 $2");
        label = PascalCaseFixerRegex2().Replace(label, "$1 $2");
        prettyLabels[name] = label;
        return label;
    }
}
