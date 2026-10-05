using System.ClientModel;
using Microsoft.ML.OnnxRuntimeGenAI;

namespace ElGuerre.Tendero.AI.Chat;

/// <summary>
/// What a model failing looks like, from each kind of client: the network, a
/// timeout, a provider's error status, the runtime in process. A caller tells these
/// from its own bugs — the first are worth another profile, the second are not.
/// Here because only this folder knows the clients' types (ADR 0053).
/// </summary>
public static class ModelFailures
{
    public static bool IsModelFailure(Exception failure) => failure is
        HttpRequestException or TimeoutException or OperationCanceledException
        or ClientResultException or OnnxRuntimeGenAIException or UnusableAnswerException;
}

/// <summary>
/// The model answered, and what it wrote cannot be used: not the shape it was asked
/// for, or nothing left of it once code had checked it (phase 12b). It is the
/// model's failure, not the shop's — another profile may do better — so it is thrown
/// by whoever read the answer and counted with the others.
/// </summary>
public sealed class UnusableAnswerException(string message) : Exception(message);
