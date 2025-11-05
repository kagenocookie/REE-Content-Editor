using System.Reflection;
using ReeLib;

namespace ContentEditor.App;

public interface IFixedClassnameComponent
{
    abstract static string Classname { get; }
}

public interface IConstructorComponent
{
    /// <summary>
    /// Invoked when a completely new instance of the component is created. Intended to set custom default values for a component type.
    /// </summary>
    void ComponentInit();
}

/// <summary>
/// A component that supports some sort of edit mode.
/// </summary>
public interface IEditableComponent
{
    public Type EditHandlerType { get; }
    public string EditTypeID { get; }
}

public interface IEditableComponent<T> : IEditableComponent
{
    Type IEditableComponent.EditHandlerType => typeof(T);
    string IEditableComponent.EditTypeID => typeof(T).Name;
}

public class Component(GameObject gameObject, RszInstance data)
{
    public RszInstance Data { get; } = data;
    public GameObject GameObject { get; } = gameObject;

    public Transform Transform => GameObject.Transform;
    public Scene? Scene => GameObject.Scene;
    public Scene? RootScene => GameObject.Scene?.RootScene;

    public string Classname => Data.RszClass.name;
    public override string ToString() => $"{GameObject} [{Data}]";

    public virtual void CloneTo(GameObject target)
    {
        Component.Create(target, Data.Clone());
    }

    /// <summary>
    /// Activate this component's scene capabilities. Invoked whenever the component is added to an active scene or an inactive scene is activated.
    /// </summary>
    internal virtual void OnActivate() { }
    /// <summary>
    /// Deactivate this component's scene capabilities. Invoked whenever the component is removed from an active scene or the scene is made inactive.
    /// </summary>
    internal virtual void OnDeactivate() { }

    private static readonly Dictionary<RszClass, Func<GameObject, RszInstance, Component>> componentTypes = new();
    private static readonly HashSet<(GameIdentifier, Assembly)> setupGames = new();

    public static void SetupTypesForGame(GameIdentifier game, Assembly assembly, Workspace env)
    {
        if (!setupGames.Add((game, assembly))) return;

        RszParser parser;
        try {
            parser = env.RszParser;
        } catch (Exception e) {
            Logger.Error(e, "Could not setup RSZ component types");
            return;
        }

        var types = typeof(WindowHandlerFactory).Assembly.GetTypes();
        foreach (var type in types) {

            var clsAttrs = type.GetCustomAttributes<RszComponentClassAttribute>();
            if (clsAttrs.Any()) {
                if (!type.IsAssignableTo(typeof(Component))) {
                    Logger.Error($"RszComponentClass annotated class must be a sublcass of {nameof(Component)} (type {type.FullName})");
                    continue;
                }

                foreach (var attr in clsAttrs) {
                    if (attr.Games.Length > 0 && !attr.Games.Contains(game.name)) continue;

                    var cls = parser.GetRSZClass(attr.Classname);
                    if (cls == null) {
                        Logger.Debug($"Class {attr.Classname} not found for game {game}");
                        continue;
                    }

                    var matched = false;
                    foreach (var constr in type.GetConstructors()) {
                        var parr = constr.GetParameters();
                        if (parr.Length == 2 && parr[0].ParameterType == typeof(GameObject) && parr[1].ParameterType == typeof(RszInstance)) {
                            var args = new object?[2];
                            componentTypes[cls] = (GameObject obj, RszInstance instance) => {
                                args[0] = obj;
                                args[1] = instance;
                                return (Component)Activator.CreateInstance(type, args)!;
                            };
                            matched = true;
                            break;
                        }
                    }
                    if (!matched) {
                        Logger.Error("Could not find suitable constructor (GameObject gameObject, RszInstance data) for [RszComponentClass] " + type.FullName);
                    }
                }
            }
        }
    }

    /// <summary>
    /// Creates a new component and adds it to the target GameObject.
    /// </summary>
    public static Component Create(GameObject gameObject, RszInstance data)
    {
        return Create(gameObject, data, true);
    }

    /// <summary>
    /// Creates a new component and adds it to the target GameObject.
    /// </summary>
    public static TComponent Create<TComponent>(GameObject gameObject, Workspace env) where TComponent : Component, IFixedClassnameComponent
    {
        return (TComponent)Create(gameObject, env, TComponent.Classname);
    }

    /// <summary>
    /// Creates a new component and adds it to the target GameObject.
    /// </summary>
    internal static Component Create(GameObject gameObject, RszInstance data, bool triggerActivate)
    {
        Component component;
        if (componentTypes.TryGetValue(data.RszClass, out var fac)) {
            gameObject.Components.Add(component = fac.Invoke(gameObject, data));
        } else {
            gameObject.Components.Add(component = new Component(gameObject, data));
        }

        if (triggerActivate && gameObject.Scene?.IsActive == true) {
            component.OnActivate();
        }

        return component;
    }

    public static Component Create(GameObject gameObject, Workspace workspace, RszClass rszClass)
    {
        var comp = Create(gameObject, RszInstance.CreateInstance(workspace.RszParser, rszClass));
        (comp as IConstructorComponent)?.ComponentInit();
        return comp;
    }

    public static Component Create(GameObject gameObject, Workspace workspace, string classname)
    {
        return Create(gameObject, workspace, workspace.RszParser.GetRSZClass(classname)!);
    }
}
