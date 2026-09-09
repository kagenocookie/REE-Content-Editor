using ContentEditor;
using ReeLib;
using ReeLib.Common;

namespace ContentPatcher;

public abstract class IDGenerator(NestableFieldAccessor[] fields)
{
    private static readonly Dictionary<RszFieldType, IDGenerator> defaultGenerators = new() {
        [RszFieldType.S64] = new IntegerIDGenerator([new NestableFieldAccessor.PlainReturn(new RszField() { type = RszFieldType.S64 })]),
        [RszFieldType.S32] = new IntegerIDGenerator([new NestableFieldAccessor.PlainReturn(new RszField() { type = RszFieldType.S32 })]),
        [RszFieldType.U32] = new IntegerIDGenerator([new NestableFieldAccessor.PlainReturn(new RszField() { type = RszFieldType.U32 })]),
        [RszFieldType.S16] = new IntegerIDGenerator([new NestableFieldAccessor.PlainReturn(new RszField() { type = RszFieldType.S16 })]),
        [RszFieldType.U16] = new IntegerIDGenerator([new NestableFieldAccessor.PlainReturn(new RszField() { type = RszFieldType.U16 })]),
        [RszFieldType.S8] = new IntegerIDGenerator([new NestableFieldAccessor.PlainReturn(new RszField() { type = RszFieldType.S8 })]),
        [RszFieldType.U8] = new IntegerIDGenerator([new NestableFieldAccessor.PlainReturn(new RszField() { type = RszFieldType.U8 })]),
        [RszFieldType.U64] = new UlongIDGenerator([new NestableFieldAccessor.PlainReturn(new RszField() { type = RszFieldType.U64 })]),
        [RszFieldType.Guid] = new GuidIDGenerator([new NestableFieldAccessor.PlainReturn(new RszField() { type = RszFieldType.Guid })]),
        [RszFieldType.String] = new StringHashGenerator([new NestableFieldAccessor.PlainReturn(new RszField() { type = RszFieldType.String })]),
    };

    private static readonly Dictionary<uint, IDGenerator> customGenerators = new();

    private static IDGenerator CreateSingleGenerator(NestableFieldAccessor[] field) => field[0].Field.type switch {
        RszFieldType.S64 => new IntegerIDGenerator(field),
        RszFieldType.S32 => new IntegerIDGenerator(field),
        RszFieldType.U32 => new IntegerIDGenerator(field),
        RszFieldType.S16 => new IntegerIDGenerator(field),
        RszFieldType.U16 => new IntegerIDGenerator(field),
        RszFieldType.S8 => new IntegerIDGenerator(field),
        RszFieldType.U8 => new IntegerIDGenerator(field),
        RszFieldType.U64 => new UlongIDGenerator(field),
        RszFieldType.Guid => new GuidIDGenerator(field),
        RszFieldType.String => new StringHashGenerator(field),
        _ => throw new NotImplementedException(),
    };

    public abstract long GetID(object value);
    public long GetID<T>(T value) => GetID((object)value!);

    public NestableFieldAccessor[] Fields { get; } = fields;


    public static IDGenerator GetGenerator(RszFieldType fieldType)
    {
        if (defaultGenerators.TryGetValue(fieldType, out var gen)) return gen;

        throw new NotImplementedException("Unsupported ID field combination");
    }

    public static IDGenerator CreateGenerator(NestableFieldAccessor[] fields)
    {
        if (fields.Length == 1) {
            return CreateSingleGenerator(fields);
        }
        if (fields.Length == 2) {
            var gen1 = CreateSingleGenerator([fields[0]]);
            var gen2 = CreateSingleGenerator([fields[1]]);
            return new DoubleIDGenerator(gen1, gen2);
        }

        throw new NotImplementedException("Unsupported ID field combination");
    }

    public static long GenerateID<T>(RszFieldType fieldType, T value)
    {
        if (defaultGenerators.TryGetValue(fieldType, out var Generator)) {
            return Generator.GetID(value);
        }

        throw new NotImplementedException();
    }

    public static IDGenerator DefineGenerator(RszClass rszClass, NestableFieldAccessor[] fields)
    {
        return customGenerators[rszClass.typeId] = CreateGenerator(fields);
    }

    public static long GenerateID(RszInstance instance)
    {
        return GetGenerator(instance.RszClass).GetID(instance);
    }

    public static IDGenerator GetGenerator(RszClass rszClass)
    {
        if (customGenerators.TryGetValue(rszClass.typeId, out var generator)) {
            return generator;
        }
        generator = TryAutoDetermineGenerator(rszClass);
        if (generator != null) return generator;

        throw new Exception("Unsupported ID generation for RSZ class " + rszClass);
    }

    private static IDGenerator? TryAutoDetermineGenerator(RszClass rszClass)
    {
        for (int i = 0; i < rszClass.fields.Length; i++) {
            var field = rszClass.fields[i];
            if (field.type == RszFieldType.U32) {
                Logger.Debug($"Auto-determined ID field {field.name} for class {rszClass.name}");
                return DefineGenerator(rszClass, [new NestableFieldAccessor.SimpleField(rszClass, i)]);
            }
        }

        return null;
    }

    private sealed class IntegerIDGenerator(NestableFieldAccessor[] fields) : IDGenerator(fields)
    {
        public override long GetID(object value) => Convert.ToInt64(Fields[0].Get(value));
    }

    private sealed class UlongIDGenerator(NestableFieldAccessor[] fields) : IDGenerator(fields)
    {
        public override long GetID(object value) => (long)(ulong)Fields[0].Get(value)!;
    }

    private sealed class DoubleIDGenerator(IDGenerator id1, IDGenerator id2) : IDGenerator(id1.Fields.Concat(id2.Fields).ToArray())
    {
        public override long GetID(object value) => id1.GetID(value) | (id2.GetID(value) << 32);
    }

    private sealed class GuidIDGenerator(NestableFieldAccessor[] fields) : IDGenerator(fields)
    {
        public override long GetID(object value)
        {
            var guid = (Guid)Fields[0].Get(value)!;
            var span = MemoryUtils.StructureAsBytes(ref guid);
            return MurMur3HashUtils.MurMur3Hash(span);
        }
    }

    private sealed class StringHashGenerator(NestableFieldAccessor[] fields) : IDGenerator(fields)
    {
        public override long GetID(object value)
        {
            var str = (string)Fields[0].Get(value)!;
            return MurMur3HashUtils.GetHash(str);
        }
    }
}