using ContentEditor;
using ContentEditor.Core;
using ContentEditor.Editor;
using ReeLib;

namespace ContentPatcher;

public class ContentWorkspace
{
    public Workspace Env { get; }
    public ResourceManager ResourceManager { get; }
    public ContentWorkspaceData Data { get; set; } = new();
    public PatchDataContainer Config { get; }
    public BundleManager BundleManager { get; set; }
    public BundleManager? EditedBundleManager { get; private set; }
    public Bundle? CurrentBundle { get; private set; }
    public DiffHandler Diff { get; }
    public MessageManager Messages { get; }
    public GameIdentifier Game => Env.Config.Game;
    public string VersionHash { get; private set; }

    public ContentWorkspace(Workspace env, PatchDataContainer patchConfig, BundleManager? rootBundleManager = null)
    {
        Env = env;
        Config = patchConfig;
        BundleManager = rootBundleManager ?? new() { VersionHash = VersionHash };
        BundleManager.GamePath = env.Config.GamePath;
        Diff = new DiffHandler(env);
        ResourceManager = new ResourceManager(patchConfig);
        Messages = new MessageManager(env);
        if (!patchConfig.IsLoaded) {
            var valid = false;
            try {
                _ = env.RszParser;
                valid = true;
            } catch (Exception) {
                Logger.Error("RSZ files are not supported at the moment, the RSZ template json is either unconfigured or invalid.");
            }
            if (valid) patchConfig.Load(this);
        }
        VersionHash = ExeUtils.GetGameVersionHash(env);
        ResourceManager.Setup(this);
    }

    public void SetBundle(string? bundle)
    {
        if (bundle == null) {
            EditedBundleManager = null;
            if (Data.ContentBundle != null) {
                Data.ContentBundle = null;
            }
            CurrentBundle = null;
            ResourceManager.SetBundle(BundleManager, null);
            return;
        }

        if (Data.ContentBundle != bundle || EditedBundleManager == null) {
            if (Data.Name == null) Data.Name = bundle;
            Data.ContentBundle = bundle;
            EditedBundleManager = BundleManager.GetBundleSpecificManager(bundle);
            CurrentBundle = EditedBundleManager.GetBundle(bundle, null);
            ResourceManager.SetBundle(EditedBundleManager, CurrentBundle);
        }
    }

    public int SaveModifiedFiles(IRectWindow? parent = null)
    {
        Logger.Info("Saving files ...");
        SaveBundle();
        int count = 0;
        foreach (var file in ResourceManager.GetModifiedResourceFiles()) {
            if (file.Modified) {
                if (parent == null || file.References.Any(ff => ff.Parent == parent)) {
                    if (file.Save(this)) {
                        count++;
                    }
                }
            }
        }
        return count;
    }

    public void SaveBundle()
    {
        if (Data.ContentBundle == null) {
            return;
        }

        var bundle = EditedBundleManager?.GetBundle(Data.ContentBundle, null) ?? BundleManager.GetBundle(Data.ContentBundle, null);
        if (bundle == null) {
            WindowManager.Instance.ShowError($"Bundle '{Data.ContentBundle}' not found!");
            return;
        }
        if (bundle.ResourceListing != null) {
            // update the diffs for all open bundle resource files that are part of the bundle
            // we don't check for file.Modified because it can be marked as false but still be different from the current diff
            // e.g. if we manually replaced the file or undo'ed our changes
            foreach (var file in ResourceManager.GetOpenFiles()) {
                if (file.NativePath != null && bundle.TryFindResourceByNativePath(file.NativePath, out var localPath) && file.DiffHandler != null) {
                    var resourceListing = bundle.ResourceListing[localPath];
                    var newdiff = file.DiffHandler.FindDiff(file);
                    if (newdiff?.ToJsonString() != resourceListing.Diff?.ToJsonString()) {
                        resourceListing.Diff = newdiff;
                        resourceListing.DiffTime = DateTime.UtcNow;
                    }
                }
            }
        }

        var deletes = new List<int>();
        for (int i = 0; i < bundle.Entities.Count; i++) {
            var entity = bundle.Entities[i];
            if (entity is not ResourceEntity) {
                // if it wasn't updated to a ResourceEntity, no resources were activated, therefore nothing was changed
                continue;
            }

            var activeEntity = ResourceManager.GetActiveEntityInstance(entity.Type, entity.Id);
            if (activeEntity == null) {
                deletes.Add(i);
                continue;
            }

            entity.Data = activeEntity?.CalculateDiff(this);
            if (entity.Data == null) {
                deletes.Add(i);
                continue;
            }

            Logger.Debug($"Saving modified entity {entity.Label}");
        }

        deletes.Reverse();
        foreach (var del in deletes) {
            Logger.Info($"Removed entity {bundle.Entities[del].Label}");
            bundle.Entities.RemoveAt(del);
        }
        bundle.GameVersion = VersionHash;

        (EditedBundleManager ?? BundleManager).SaveBundle(bundle);
    }

    public override string ToString() => Data.Name ?? "New Workspace";
}

// serialized
public class ContentWorkspaceData
{
    public string? Name { get; set; }
    public string? ContentBundle { get; set; }
    public List<WindowData> Windows { get; set; } = new();
}
