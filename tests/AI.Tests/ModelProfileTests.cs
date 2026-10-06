using Xunit;

namespace ElGuerre.Tendero.AI.Tests;

public sealed class ModelProfileTests
{
    /// <summary>€ per million tokens, so a token costs the price in micro-euros:
    /// 1,500 in at €0.30 and 342 out at €2.50 is 450 + 855 µ€.</summary>
    [Fact]
    public void The_cost_is_the_profiles_price_in_micro_euros()
    {
        var profile = new ModelProfile { InputPrice = 0.30m, OutputPrice = 2.50m };

        Assert.Equal(1305, profile.CostMicroEuros(1500, 342));
    }

    [Fact]
    public void A_local_profile_costs_nothing() =>
        Assert.Equal(0, new ModelProfile().CostMicroEuros(4000, 800));

    [Fact]
    public void A_cost_rounds_half_away_from_zero() =>
        Assert.Equal(1, new ModelProfile { InputPrice = 0.5m }.CostMicroEuros(1, 0));

    /// <summary>A profile with no endpoint is not available in this host — the local
    /// one until the AppHost hands it an address — and that is not a mistake
    /// (final review, I-1). One that is half configured is.</summary>
    [Fact]
    public void A_profile_with_no_endpoint_is_not_here_and_is_no_problem()
    {
        var profile = new ModelProfile { Model = "qwen3.5-4b-q8_0" };

        Assert.False(profile.IsAvailable);
        Assert.Empty(profile.Problems("local"));
    }

    [Theory]
    [InlineData(ModelKind.OpenAICompatible, "http://x/v1/", null, ModelAuth.None, null, "Model")]
    [InlineData(ModelKind.OpenAICompatible, "http://x/v1/", "m", ModelAuth.Bearer, null, "ApiKey")]
    public void A_profile_that_cannot_be_called_names_what_it_lacks(
        ModelKind kind, string? endpoint, string? model, ModelAuth auth, string? key, string lacking)
    {
        var profile = new ModelProfile
        {
            Kind = kind,
            Endpoint = endpoint is null ? null : new Uri(endpoint),
            Model = model,
            Auth = auth,
            ApiKey = key,
        };

        Assert.Contains(profile.Problems("gemini"), problem => problem.Contains(lacking) && problem.Contains("gemini"));
    }

    /// <summary>Said at start, not at the first call (M-7).</summary>
    [Theory]
    [InlineData("http://x/v1/", 0, "TimeoutSeconds")]
    [InlineData("ftp://x/v1/", 120, "Endpoint")]
    public void A_profile_with_an_impossible_timeout_or_endpoint_names_it(string endpoint, int timeout, string lacking)
    {
        var profile = new ModelProfile { Endpoint = new Uri(endpoint), Model = "m", TimeoutSeconds = timeout };

        Assert.Contains(profile.Problems("local"), problem => problem.Contains(lacking));
    }

    [Fact]
    public void A_profile_with_everything_has_no_problems() =>
        Assert.Empty(new ModelProfile { Endpoint = new Uri("http://x/v1/"), Model = "m" }.Problems("local"));

    [Fact]
    public void A_scripted_profile_needs_a_script_that_exists()
    {
        Assert.NotEmpty(new ModelProfile { Kind = ModelKind.Scripted, Script = "no/such/script.json" }.Problems("scripted"));
        Assert.Empty(new ModelProfile { Kind = ModelKind.Scripted, Script = Path.Combine(AppContext.BaseDirectory, "scripts", "contract.json") }.Problems("scripted"));
    }

    [Fact]
    public void An_onnx_profile_needs_a_directory_and_no_endpoint()
    {
        Assert.NotEmpty(new ModelProfile { Kind = ModelKind.OnnxInProcess }.Problems("phi"));
        Assert.Empty(new ModelProfile { Kind = ModelKind.OnnxInProcess, Directory = "/models/phi" }.Problems("phi"));
    }
}
