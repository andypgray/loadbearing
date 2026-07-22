namespace Meridian.Interchange.Partners;

/// <summary>
///     The wire contract handed to a partner client — the transport-facing shape that carries one
///     message's id and serialized payload. It decouples the partner contract from the persisted
///     OutboxMessage, so how a message is stored can change without reshaping what partners see.
/// </summary>
public sealed record PartnerEnvelope(string MessageId, string Payload);
