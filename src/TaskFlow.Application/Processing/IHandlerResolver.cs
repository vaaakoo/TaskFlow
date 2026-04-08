using System;

namespace TaskFlow.Application.Processing;

public interface IHandlerResolver
{
    /// <summary>
    /// Resolves the actual .NET Type associated with the string payloadType.
    /// This allows the JobProcessor to cleanly deserialize the JSON without magic strings.
    /// </summary>
    Type ResolvePayloadType(string payloadType);

    /// <summary>
    /// Resolves the correct handler instance for the given payload type.
    /// </summary>
    IJobHandler ResolveHandler(string payloadType);
}
