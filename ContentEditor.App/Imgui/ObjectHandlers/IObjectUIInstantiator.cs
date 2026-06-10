using ContentPatcher;

namespace ContentEditor.App;

public interface IObjectUIInstantiator
{
    static abstract Func<EntityField, IObjectUIHandler> GetFactory();
}
