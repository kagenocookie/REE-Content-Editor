namespace ContentPatcher;

public static class RWLockUtils
{
    public static void ReadCallback(this ReaderWriterLockSlim rwlock, Action func)
    {
        rwlock.EnterReadLock();
        try {
            func.Invoke();
        } finally {
            rwlock.ExitReadLock();
        }
    }

    public static TValue Read<TValue>(this ReaderWriterLockSlim rwlock, Func<TValue> func)
    {
        rwlock.EnterReadLock();
        var value = func();
        rwlock.ExitReadLock();
        return value;
    }

    public static TValue ReadSafe<TValue>(this ReaderWriterLockSlim rwlock, Func<TValue> func)
    {
        rwlock.EnterReadLock();
        try {
            return func();
        } finally {
            rwlock.ExitReadLock();
        }
    }

    public static TValue Read<TContext, TValue>(this ReaderWriterLockSlim rwlock, TContext ctx, Func<TContext, TValue> func)
    {
        rwlock.EnterReadLock();
        var value = func(ctx);
        rwlock.ExitReadLock();
        return value;
    }

    public static TValue ReadSafe<TContext, TValue>(this ReaderWriterLockSlim rwlock, TContext ctx, Func<TContext, TValue> func)
    {
        rwlock.EnterReadLock();
        try {
            return func(ctx);
        } finally {
            rwlock.ExitReadLock();
        }
    }

    public static void Write<TValue>(this ReaderWriterLockSlim rwlock, TValue ctx, Action<TValue> func)
    {
        rwlock.EnterWriteLock();
        func(ctx);
        rwlock.ExitWriteLock();
    }

    public static void WriteSafe<TValue>(this ReaderWriterLockSlim rwlock, TValue ctx, Action<TValue> func)
    {
        rwlock.EnterWriteLock();
        try {
            func(ctx);
        } finally {
            rwlock.ExitWriteLock();
        }
    }

}
