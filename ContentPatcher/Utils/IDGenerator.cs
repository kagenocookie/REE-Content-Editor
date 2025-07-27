using ReeLib;
using ReeLib.Common;

namespace ContentPatcher;

public abstract class IDGenerator
{
    private static readonly IntegerIDGenerator IntGenerator = new IntegerIDGenerator();
    private static readonly Dictionary<uint, IDGenerator> generators = new() {
        [(uint)RszFieldType.S32] = IntGenerator,
        [(uint)RszFieldType.U32] = IntGenerator,
        [(uint)RszFieldType.S64] = IntGenerator,
        [(uint)RszFieldType.S16] = IntGenerator,
        [(uint)RszFieldType.U16] = IntGenerator,
        [(uint)RszFieldType.S8] = IntGenerator,
        [(uint)RszFieldType.U8] = IntGenerator,
        [(uint)RszFieldType.U64] = new UlongIDGenerator(),
        [(uint)RszFieldType.Guid] = new GuidIDGenerator(),
        [(uint)RszFieldType.String] = new StringHashGenerator(),
    };

    public long GetID(RszInstance instance, int[] fieldIndices) => GetID(instance.Values, fieldIndices);
    public abstract long GetID(object[] values, int[] fieldIndices);
    public long GetID<T>(T value) => GetID([value!], [0]);

    public static IDGenerator GetGenerator(uint generatorTypeHash)
    {
        if (generators.TryGetValue(generatorTypeHash, out var gen)) return gen;

        throw new NotImplementedException("Unsupported ID field combination");
    }

    public static IDGenerator GetGenerator(RszInstance instance, int[] fieldIndices)
    {
        var fields = instance.RszClass.fields;
        if (fieldIndices.Length == 1 && generators.TryGetValue((uint)fields[fieldIndices[0]].type, out var result)) {
            return result;
        }
        if (fieldIndices.Length == 2) {
            uint t1 = (uint)fields[fieldIndices[0]].type;
            uint t2 = (uint)fields[fieldIndices[1]].type;
            uint typehash = (uint)HashCode.Combine(t1, t2);
            if (generators.TryGetValue(typehash, out result)) {
                return result;
            }
            var gen1 = generators[t1];
            var gen2 = generators[t2];
            return generators[typehash] = new DoubleIDGenerator(gen1, gen2);
        }

        throw new NotImplementedException("Unsupported ID field combination");
    }

    public static long GenerateID<T>(uint generatorTypeHash, T value)
    {
        if (generators.TryGetValue(generatorTypeHash, out var Generator)) {
            return Generator.GetID(value);
        }

        throw new NotImplementedException();
    }

    public static long GenerateID(uint typeHash, object[] values, int[] fieldIndices)
    {
        if (generators.TryGetValue(typeHash, out var Generator)) {
            return Generator.GetID(values, fieldIndices);
        }

        throw new NotImplementedException();
    }

    public static long GenerateID(RszInstance instance, int[] fieldIndices)
    {
        if (generators.TryGetValue(instance.RszClass.crc, out var Generator)) {
            return Generator.GetID(instance, fieldIndices);
        }

        generators[instance.RszClass.crc] = Generator = GetGenerator(instance, fieldIndices);
        return Generator.GetID(instance, fieldIndices);
    }

    private sealed class IntegerIDGenerator : IDGenerator
    {
        public override long GetID(object[] values, int[] fieldIndices) => Convert.ToInt64(values[fieldIndices[0]]);
    }

    private sealed class UlongIDGenerator : IDGenerator
    {
        public override long GetID(object[] values, int[] fieldIndices) => (long)(ulong)values[fieldIndices[0]];
    }

    private sealed class DoubleIDGenerator(IDGenerator id1, IDGenerator id2) : IDGenerator
    {
        public override long GetID(object[] values, int[] fieldIndices) => id1.GetID(values[fieldIndices[0]]) | (id2.GetID(values[fieldIndices[1]]) << 32);
    }

    private sealed class GuidIDGenerator : IDGenerator
    {
        public override long GetID(object[] values, int[] fieldIndices)
        {
            var guid = (Guid)values[fieldIndices[0]];
            var span = MemoryUtils.StructureAsBytes(ref guid);
            return MurMur3HashUtils.MurMur3Hash(span);
        }
    }

    private sealed class StringHashGenerator : IDGenerator
    {
        public override long GetID(object[] values, int[] fieldIndices)
        {
            var str = (string)values[fieldIndices[0]];
            return MurMur3HashUtils.GetHash(str);
        }
    }

    private sealed class FixedFuncGenerator(Func<object[], int[], long> func) : IDGenerator
    {
        public override long GetID(object[] values, int[] fieldIndices) => func.Invoke(values, fieldIndices);
    }
}