namespace ElGuerre.Tendero.Assist.Turns;

/// <summary>What a turn cost, at the answering profile's price: a panel's cost line adds these up.</summary>
public sealed record TurnCost(int Calls, long InputTokens, long OutputTokens, long MicroEuros, int Milliseconds, string Model);

/// <summary>One message of a conversation, as the client posts it: the server keeps none.</summary>
/// <param name="Role">"user" or "assistant".</param>
public sealed record TurnMessage(string Role, string Text);
