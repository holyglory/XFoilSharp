using System;
using System.Runtime.CompilerServices;
using System.Threading;

// Legacy audit:
// Primary legacy source: none
// Secondary legacy source: none
// Role in port: Managed ambient trace hook that lets core-only code participate in parity diagnostics without depending on solver assemblies.
// Differences: Legacy XFoil does not have an equivalent structured trace scope abstraction; this is a .NET-only instrumentation layer.
// Decision: Keep the managed tracing utility because it is essential for parity debugging infrastructure and has no direct legacy analogue.
namespace XFoil.Core.Diagnostics;

/// <summary>
/// Minimal ambient trace hook for core-only stages that need to participate in
/// parity diagnostics without depending on the solver diagnostics assembly.
/// </summary>
public static class CoreTrace
{
    private static readonly AsyncLocal<Action<string, string, object?>?> CurrentWriter = new();

    public static bool IsEnabled => CurrentWriter.Value is not null;

    // Legacy mapping: none; this is a managed-only trace writer installation hook.
    // Difference from legacy: The port uses an ambient async-local callback instead of global debug prints or ad hoc instrumentation.
    // Decision: Keep the managed hook because it cleanly enables or disables parity tracing at API boundaries.
    public static IDisposable Begin(Action<string, string, object?>? writer)
    {
        if (writer is null)
        {
            return EmptyScope.Instance;
        }

        Action<string, string, object?>? previous = CurrentWriter.Value;
        CurrentWriter.Value = writer;
        return new RestoreScope(previous);
    }

    // Legacy mapping: none; this is a managed-only trace-scope helper.
    // Difference from legacy: The method emits structured enter/exit events instead of relying on manual instrumentation inserted around a call site.
    // Decision: Keep the scope helper because it makes parity traces consistent across the managed port.
    public static IDisposable Scope(string scope, object? inputs = null)
    {
        Action<string, string, object?>? writer = CurrentWriter.Value;
        if (writer is null)
        {
            return EmptyScope.Instance;
        }

        writer("call_enter", scope, inputs);
        return new TraceScope(writer, scope);
    }

    // Legacy mapping: none; this is a managed-only trace event forwarder.
    // Difference from legacy: The port centralizes event emission through one ambient hook instead of direct writes from each call site.
    // Decision: Keep the event helper because it reduces tracing boilerplate.
    public static void Event(string kind, string scope, object? data = null)
    {
        CurrentWriter.Value?.Invoke(kind, scope, data);
    }

    // Legacy mapping: none; this is a managed naming helper for trace scopes.
    // Difference from legacy: Legacy XFoil does not synthesize scope names from type/member metadata.
    // Decision: Keep the helper because it provides stable, readable trace scope identifiers.
    public static string ScopeName(Type owner, [CallerMemberName] string memberName = "")
    {
        return string.IsNullOrWhiteSpace(memberName)
            ? owner.Name
            : $"{owner.Name}.{memberName}";
    }

    private sealed class RestoreScope : IDisposable
    {
        private readonly Action<string, string, object?>? _previous;
        private int _disposed;

        // Legacy mapping: none; this is a managed trace-state restoration helper.
        // Difference from legacy: The port stores the previous callback in an IDisposable scope object instead of mutating one global flag in place.
        // Decision: Keep the scope object because it makes nested tracing safe.
        public RestoreScope(Action<string, string, object?>? previous)
        {
            _previous = previous;
        }

        // Legacy mapping: none; this is a managed cleanup hook for the trace scope.
        // Difference from legacy: Disposal restores the prior ambient writer atomically, which has no direct analogue in the Fortran codebase.
        // Decision: Keep the idempotent cleanup because it protects nested trace scopes.
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            CurrentWriter.Value = _previous;
        }
    }

    private sealed class TraceScope : IDisposable
    {
        private readonly Action<string, string, object?> _writer;
        private readonly string _scope;
        private int _disposed;

        // Legacy mapping: none; this is a managed enter/exit scope holder.
        // Difference from legacy: The scope object stores the callback and name explicitly rather than relying on paired manual trace statements.
        // Decision: Keep the helper object because it standardizes trace lifetimes.
        public TraceScope(Action<string, string, object?> writer, string scope)
        {
            _writer = writer;
            _scope = scope;
        }

        // Legacy mapping: none; this is a managed trace-scope exit hook.
        // Difference from legacy: Disposal emits a structured `call_exit` event, which is a .NET tracing convention rather than legacy solver behavior.
        // Decision: Keep the cleanup hook because it guarantees balanced trace events.
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            _writer("call_exit", _scope, null);
        }
    }

    private sealed class EmptyScope : IDisposable
    {
        public static readonly EmptyScope Instance = new();

        // Legacy mapping: none; this is a managed no-op tracing placeholder.
        // Difference from legacy: Legacy XFoil has no equivalent disposable sentinel object.
        // Decision: Keep the no-op scope because it eliminates null handling in callers.
        public void Dispose()
        {
        }
    }
}
