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

    public abstract long GetID(object value, NestableFieldAccessor[] fields);
    public long GetID<T>(T value) => GetID((object)value!, PlainReturnAccessorArray);
    private static readonly NestableFieldAccessor[] PlainReturnAccessorArray = [NestableFieldAccessor.PlainReturn.Instance];

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

    public static IDGenerator GetGenerator(object instance, NestableFieldAccessor[] fields)
    {
        if (fields.Length == 1 && generators.TryGetValue((uint)fields[0].Field.type, out var result)) {
            return result;
        }
        if (fields.Length == 2) {
            uint t1 = (uint)fields[0].Field.type;
            uint t2 = (uint)fields[1].Field.type;
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

    public static long GenerateID(RszInstance instance, NestableFieldAccessor[] fields)
    {
        if (generators.TryGetValue(instance.RszClass.crc, out var Generator)) {
            return Generator.GetID(instance, fields);
        }

        generators[instance.RszClass.crc] = Generator = GetGenerator(instance, fields);
        return Generator.GetID(instance, fields);
    }

    private sealed class IntegerIDGenerator : IDGenerator
    {
        public override long GetID(object value, NestableFieldAccessor[] fields) => Convert.ToInt64(fields[0].Get(value));
    }

    private sealed class UlongIDGenerator : IDGenerator
    {
        public override long GetID(object value, NestableFieldAccessor[] fields) => (long)(ulong)fields[0].Get(value)!;
    }

    private sealed class DoubleIDGenerator(IDGenerator id1, IDGenerator id2) : IDGenerator
    {
        public override long GetID(object value, NestableFieldAccessor[] fields) => id1.GetID(fields[0].Get(value)) | (id2.GetID(fields[1].Get(value)) << 32);
    }

    private sealed class GuidIDGenerator : IDGenerator
    {
        public override long GetID(object value, NestableFieldAccessor[] fields)
        {
            var guid = (Guid)fields[0].Get(value)!;
            var span = MemoryUtils.StructureAsBytes(ref guid);
            return MurMur3HashUtils.MurMur3Hash(span);
        }
    }

    private sealed class StringHashGenerator : IDGenerator
    {
        public override long GetID(object value, NestableFieldAccessor[] fields)
        {
            var str = (string)fields[0].Get(value)!;
            return MurMur3HashUtils.GetHash(str);
        }
    }
}