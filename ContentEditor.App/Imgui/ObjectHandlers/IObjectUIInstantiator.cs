using ContentPatcher;

namespace ContentEditor.App;

public interface IObjectUIInstantiator
{
    static abstract Func<CustomField, IObjectUIHandler> GetFactory();
}
