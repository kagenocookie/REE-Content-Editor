using System.Diagnostics;
using System.Reflection;
using System.Text;
using ContentEditor;
using ReeLib;
using ReeLib.Common;
using ReeLib.Efx;
using ReeLib.Efx.Structs.Common;
using ReeLib.Efx.Structs.Main;
using ReeLib.Efx.Structs.Misc;

using Ukn = ReeLib.UndeterminedFieldType;

namespace ContentEditor.Reversing;

internal static class EfxReversingTools
{
    private static GameIdentifier[] EfxSupportedGames = [
        GameIdentifier.re7,
        GameIdentifier.dmc5,
        GameIdentifier.re4,
        GameIdentifier.dd2,
        GameIdentifier.re2,
        GameIdentifier.re3,
        GameIdentifier.re8,
        GameIdentifier.re2rt,
        GameIdentifier.re3rt,
        GameIdentifier.re7rt,
        // GameIdentifier.MonsterHunterRise,
        // GameIdentifier.StreetFighter6,
        GameIdentifier.mhwilds,
        // GameIdentifier.pragmata,
    ];

    public static Func<GameIdentifier, string, IEnumerable<(string path, Stream file)>>? FileProvider { get; set; }

    private static readonly string EfxOutputBasePath = Path.Combine(AppContext.BaseDirectory, "efx_output");

    public static void FullReadWriteTest()
    {
        Thread.CurrentThread.CurrentCulture = System.Globalization.CultureInfo.InvariantCulture;

        Directory.CreateDirectory(EfxOutputBasePath);

        // FullExpressionParseTest();
        FullReadTest();
        DumpEfxAttributeUsageList();
        // DumpEfxStructValueLists<EFXAttributeSpawn>();
        DumpEfxStructValueLists();

        // var unknownHashes = EfxFile.UnknownParameterHashes;
        // var foundHashes = EfxFile.FoundNamedParameterHashes;
        // var eznames = unknownHashes.Select(hash => foundHashes.TryGetValue(hash, out var name) ? $"{hash}={name}" : null).Where(x => x != null).ToArray();

        var fieldInconsistencies = FindInconsistentEfXFields();

        if (fieldInconsistencies.Count > 0) {
            Logger.Info("Found field type inconsistencies:");
        }
        HashSet<Type> ignoreTypes = [typeof(EFXAttributeFixRandomGenerator), typeof(EFXAttributeUnitCulling)];
        HashSet<string> ignoreFieldNames = ["mdfPropertyHash"];
        foreach (var (attrType, inco) in fieldInconsistencies.OrderBy(fi => fi.Key.FullName)) {
            if (ignoreTypes.Contains(attrType)) continue;
            foreach (var (field, data) in inco.OrderBy(o => o.Key)) {
                if (ignoreFieldNames.Contains(field)) continue;
                Logger.Info($"{attrType.Name}: {field}\n{string.Join(", ", data.values)}");
            }
            Logger.Info("");
        }
    }

    private static void ExecuteFullReadTest(string fileExt, Action<GameIdentifier, string, Stream> func)
    {
        Debug.Assert(FileProvider != null);
        foreach (var game in EfxSupportedGames) {
            try {
                foreach (var (file, path) in FileProvider.Invoke(game, fileExt)) {
                    func.Invoke(game, file, path);
                }
            } catch (Exception e) {
                Logger.Warn($"Failed to handle files for {game}: {e.Message}");
            }
        }
    }

    private static void FullReadTest()
    {
        ExecuteFullReadTest("efx", (game, filepath, stream) => {
            var file = new EfxFile(new FileHandler(stream, filepath));
            try {
                file.Read();

                if (file.FileHandler.Position != file.FileHandler.Stream.Length) Logger.Error("File was not fully read");
                file.ParseExpressions();
            } catch (Exception e) {
                Logger.Error("Failed file " + Path.GetFileName(filepath) + ": " + e.Message + "/n" + filepath);
                return;
            }
        });
    }

    private static void FullExpressionParseTest()
    {
        ExecuteFullReadTest("efx", (game, filepath, stream) => {
            var file = new EfxFile(new FileHandler(stream, filepath));
            try {
                file.Read();
                file.ParseExpressions();
                foreach (var a in file.GetAttributesAndActions(true)) {
                    if (a is IExpressionAttribute expr1 && expr1.Expression?.ParsedExpressions != null) {
                        VerifyExpressionCorrectness(game, filepath, file, expr1.Expression);
                    }
                    if (a is IMaterialExpressionAttribute expr2 && expr2.MaterialExpressions?.ParsedExpressions != null) {
                        VerifyExpressionCorrectness(game, filepath, file, expr2.MaterialExpressions);
                    }
                }
            } catch (Exception e) {
                Logger.Error("Failed file " + Path.GetFileName(filepath) + ": " + e.Message + "/n" + filepath);
                return;
            }

            void VerifyExpressionCorrectness(GameIdentifier game, string filepath, EfxFile file, EFXExpressionContainer a)
            {
                var parsedlist = (a as EFXExpressionList)?.ParsedExpressions ?? (a as EFXMaterialExpressionList)!.ParsedExpressions!;
                var srclist = (a as EFXExpressionList)?.Expressions ?? (a as EFXMaterialExpressionList)!.Expressions!;
                for (var i = 0; i < parsedlist.Count; i++) {
                    var srcExp = srclist.ElementAt(i);
                    var parsed = parsedlist[i];
                    var originalStr = parsed.ToString();

                    var tree = EfxExpressionStringParser.Parse(originalStr, parsed.parameters);
                    var reParsedStr = tree.ToString();
                    // TODO: properly handle material expression additional fields
                    DataInterpretationException.DebugWarnIf(reParsedStr != originalStr, reParsedStr);
                    var reFlattened = file.FlattenExpressionTree(tree);
                    if (false == reFlattened.parameters?.OrderBy(p => p.parameterNameHash).SequenceEqual(srcExp.parameters?.OrderBy(p => p.parameterNameHash)!)) {
                        Logger.Error("Expression might not have re-serialized correctly!");
                        return;
                    }

                    // we can't do full 100% per-component comparison because we lost parentheses during the tostring conversion
                    // in other words, `a + (b + c)` would get re-serialized as `(a + b) + c`, which isn't a meaningful difference content wise but would serialize differently
                    if (game == GameIdentifier.dmc5 && (
                        Path.GetFileName(filepath.AsSpan()).SequenceEqual("efd_03_l03_018_00.efx.1769672") ||
                        Path.GetFileName(filepath.AsSpan()).SequenceEqual("efd_03_l03_011_00.efx.1769672")
                    )) {
                        // let these be incomplete because they're the only files that have this issue
                    } else {
                        DataInterpretationException.DebugWarnIf(reFlattened.components.Count != srcExp.components.Count);
                    }
                }
            }
        });
    }

    private static Dictionary<Type, Dictionary<string, (HashSet<string> values, HashSet<string> filepaths)>> FindInconsistentEfXFields()
    {

        var fieldInconsistencies = new Dictionary<Type, Dictionary<string, (HashSet<string> values, HashSet<string> filepaths)>>();
        void AddInconsistency(Type type, string field, string valueInfo, string filepath)
        {
            if (!fieldInconsistencies.TryGetValue(type, out var dict)) {
                fieldInconsistencies[type] = dict = new();
            }

            if (!dict.TryGetValue(field, out var data)) {
                dict[field] = data = new() { filepaths = new(), values = new() };
            }

            data.values.Add(valueInfo);
            data.filepaths.Add(filepath);
        }

        ExecuteFullReadTest("efx", (game, filepath, stream) => {
            var file = new EfxFile(new FileHandler(stream, filepath));
            try {
                file.Read();
                if (file.FileHandler.Position != file.FileHandler.Stream.Length) Logger.Error("File was not fully read");
            } catch (Exception e) {
                Logger.Error("Failed file " + Path.GetFileName(filepath) + ": " + e.Message + "/n" + filepath);
                return;
            }

            static bool LooksLikeFloat(int n) => BitConverter.Int32BitsToSingle(n) is float f && MathF.Abs(f) > 0.00001f && MathF.Abs(f) < 10000f;

            foreach (var a in file.GetAttributesAndActions(true)) {
                var attrType = a.GetType();
                var fields = attrType.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                foreach (var f in fields) {
                    if (f.FieldType == typeof(Ukn) && f.GetValue(a) is Ukn uu && uu.value != 0) {
                        AddInconsistency(attrType, f.Name, uu.ToString(), filepath);
                    } else if (f.FieldType == typeof(int) && f.GetValue(a) is int ii && LooksLikeFloat(ii)) {
                        AddInconsistency(attrType, f.Name, BitConverter.Int32BitsToSingle(ii).ToString("0.0#"), filepath);
                    } else if (f.FieldType == typeof(uint) && f.GetValue(a) is uint n && LooksLikeFloat((int)n) && !f.Name.Contains("hash", StringComparison.OrdinalIgnoreCase) && !f.Name.Contains("mask", StringComparison.OrdinalIgnoreCase)) {
                        AddInconsistency(attrType, f.Name, BitConverter.Int32BitsToSingle((int)n).ToString("0.0#"), filepath);
                    } else if (f.FieldType == typeof(float) && f.GetValue(a) is float flt &&
                        (Math.Abs(flt) > 100000000 && !float.IsInfinity(flt) || flt != 0 && BitConverter.SingleToUInt32Bits(flt) < 1000)
                    ) {
                        AddInconsistency(attrType, f.Name, new Ukn(flt).ToString(), filepath);
                    } else if (f.FieldType.IsEnum && !f.FieldType.IsEnumDefined(f.GetValue(a)!)) {
                        AddInconsistency(attrType, f.Name, new Ukn(Convert.ToInt32(f.GetValue(a))).ToString(), filepath);
                    }
                }
            }
        });

        return fieldInconsistencies;
    }

    private static void DumpEfxAttributeUsageList()
    {
        var attributeTypeUsages = new Dictionary<EfxVersion, Dictionary<EfxAttributeType, HashSet<string>>>();
        ExecuteFullReadTest("efx", (game, filepath, stream) => {
            var file = new EfxFile(new FileHandler(stream, filepath));
            try {
                file.Read();
            } catch (Exception e) {
                Logger.Error("Failed file " + Path.GetFileName(filepath) + ": " + e.Message + "/n" + filepath);
                return;
            }

            foreach (var attr in file.GetAttributesAndActions(true)) {
                if (!attributeTypeUsages.TryGetValue(file.Header!.Version, out var verUsages)) {
                    attributeTypeUsages[file.Header!.Version] = verUsages = new();
                }
                if (!verUsages.TryGetValue(attr.type, out var usages)) {
                    verUsages[attr.type] = usages = new();
                }
                usages.Add(filepath);
            }
        });

        var usageSb = new StringBuilder();
        foreach (var (version, attrPaths) in attributeTypeUsages.OrderBy(atu => atu.Key)) {
            usageSb
                .AppendLine("----------------------------------")
                .Append("Game: ").AppendLine(version.ToString())
                .AppendLine();

            foreach (var (attr, paths) in attrPaths.OrderBy(k => EfxAttributeTypeRemapper.ToAttributeTypeID(version, k.Key))) {
                usageSb.Append($"{EfxAttributeTypeRemapper.ToAttributeTypeID(version, attr)} = \t").Append(attr.ToString()).AppendLine($" ({paths.Count})");
            }


            usageSb.AppendLine();
            usageSb.AppendLine("Unmapped:");
            foreach (var val in Enum.GetValues<EfxAttributeType>()) {
                if (!EfxAttributeTypeRemapper.HasAttributeType(version, val)) {
                    usageSb.Append(val).Append(" = {").Append(string.Join(", ", val.GetVersionsOfType().Select((verId => verId.version + " = " + verId.typeId)))).AppendLine(" }");
                }
            }
            usageSb.AppendLine("----------------------------------");

            foreach (var (attr, paths) in attrPaths.OrderBy(k => EfxAttributeTypeRemapper.ToAttributeTypeID(version, k.Key))) {
                usageSb.AppendLine().Append(attr.ToString()).AppendLine($" ({paths.Count})");
                foreach (var path in paths.OrderBy(x => x)) {
                    usageSb.AppendLine(path);
                }
            }
            usageSb.AppendLine();
        }
        File.WriteAllText(Path.Combine(EfxOutputBasePath, "__usages.txt"), usageSb.ToString());
    }


    private static void DumpEfxStructValueLists<T>() where T : EFXAttribute
        => DumpEfxStructValueLists(typeof(T));

    private static void DumpEfxStructValueLists(Type? targetType = null)
    {
        var dict = new Dictionary<Type, Dictionary<string, Dictionary<EfxVersion, HashSet<string>>>>();
        ExecuteFullReadTest("efx", (game, filepath, stream) => {
            var file = new EfxFile(new FileHandler(stream, filepath));
            try {
                file.Read();
            } catch (Exception e) {
                Logger.Error("Failed file " + Path.GetFileName(filepath) + ": " + e.Message + "/n" + filepath);
                return;
            }

            foreach (var attr in file.GetAttributesAndActions(true)) {
                var attrType = attr.GetType();
                if (targetType != null && attrType != targetType) continue;
                HandleObjectValuePrint(dict, file, attr);
            }

            static void HandleObjectValuePrint(Dictionary<Type, Dictionary<string, Dictionary<EfxVersion, HashSet<string>>>> dict, EfxFile file, object target)
            {
                var targetType = target.GetType();
                if (targetType.Namespace?.StartsWith("System") == true) return;

                if (!dict.TryGetValue(targetType, out var allValues)) {
                    dict[targetType] = allValues = new();
                }

                var fieldInfos = targetType.IsValueType ? targetType.GetFields(BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic)!
                    : EfxTools.GetFieldInfo(targetType, file.Header!.Version)
                        .Select(pair => targetType.GetField(pair.name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!)
                        .Where(f => f != null);

                foreach (var fi in fieldInfos) {

                    if (!allValues.TryGetValue(fi.Name, out var values)) {
                        allValues[fi.Name] = values = new();
                    }
                    if (!values.TryGetValue(file.Header!.Version, out var fieldValues)) {
                        values[file.Header!.Version] = fieldValues = new();
                    }
                    var value = fi.GetValue(target);
                    if (value is int i) fieldValues.Add(new Ukn(i).GetMostLikelyValueTypeString());
                    else if (value is uint u) fieldValues.Add(new Ukn(u).GetMostLikelyValueTypeString());
                    else if (value is float f) fieldValues.Add(new Ukn(f).GetMostLikelyValueTypeString());
                    else if (value is ReeLib.via.Color c) fieldValues.Add(new Ukn(c.rgba).GetMostLikelyValueTypeString());
                    else if (value is Ukn uu) fieldValues.Add(uu.GetMostLikelyValueTypeString());
                    else {
                        fieldValues.Add(value?.ToString() ?? "NULL");
                        if (value != null) {
                            if (fi.FieldType.IsAssignableTo(typeof(BaseModel))) {
                                HandleObjectValuePrint(dict, file, value);
                            } else if (fi.FieldType.IsArray) {
                                foreach (var it in (Array)value!) { HandleObjectValuePrint(dict, file, it); }
                            } else if (fi.FieldType.IsGenericType && fi.FieldType.GetGenericTypeDefinition() == typeof(List<>)) {
                                foreach (var it in (System.Collections.IList)value!) { HandleObjectValuePrint(dict, file, it); }
                            }
                        }
                    }
                }
            }
        });

        var usageSb = new StringBuilder();
        foreach (var (attrType, allValues) in dict) {
            usageSb.Clear();
            foreach (var (field, versions) in allValues.OrderBy(atu => attrType.GetFields(BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic).Select(f => f.Name).ToList().IndexOf(atu.Key))) {
                usageSb
                    .AppendLine("----------------------------------")
                    .Append("Field: ").AppendLine(field.ToString())
                    .AppendLine();

                foreach (var (version, values) in versions.OrderBy(k => k.Key)) {
                    usageSb.Append(version.ToString()).Append(": ").AppendLine(string.Join(", ", values));
                }
                usageSb.AppendLine();
            }

            File.WriteAllText(Path.Combine(
                EfxOutputBasePath,
                $"{attrType.Name.Replace("EFXAttribute", "")}__{attrType.Namespace}.txt"
            ), usageSb.ToString());
        }
        Logger.Info($"EFX dump written to {EfxOutputBasePath}");
    }
}