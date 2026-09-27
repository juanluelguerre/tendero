using ElGuerre.Tendero.SharedKernel.Tests.Slices.Chosen;
using ElGuerre.Tendero.SharedKernel.Tests.Slices.Other;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ElGuerre.Tendero.SharedKernel.Tests
{
    /// <summary>
    /// One vertical slice, registered by its namespace (P10-6): the worker takes
    /// Catalog's <c>SetProductAttribute</c> without the import and its connectors,
    /// which <c>ValidateOnBuild</c> would refuse to build. A slice is a folder, and
    /// a folder is a namespace — so the namespace is the unit.
    /// </summary>
    public sealed class SliceRegistrationTests
    {
        [Fact]
        public void A_slice_brings_its_own_handler_and_validator_and_nothing_from_the_next_folder()
        {
            using var provider = new ServiceCollection()
                .AddTenderoCqrs()
                .AddTenderoSlices(typeof(ChosenCommand))
                .BuildServiceProvider();
            using var scope = provider.CreateScope();

            Assert.IsType<ChosenHandler>(scope.ServiceProvider.GetRequiredService<ICommandHandler<ChosenCommand, int>>());
            Assert.IsType<ChosenValidator>(Assert.Single(scope.ServiceProvider.GetServices<IValidator<ChosenCommand>>()));
            Assert.Null(scope.ServiceProvider.GetService<ICommandHandler<OtherCommand, int>>());
        }

        [Fact]
        public void Naming_the_same_slice_twice_registers_it_once()
        {
            using var provider = new ServiceCollection()
                .AddTenderoCqrs()
                .AddTenderoSlices(typeof(ChosenCommand), typeof(ChosenHandler))
                .BuildServiceProvider();
            using var scope = provider.CreateScope();

            Assert.Single(scope.ServiceProvider.GetServices<ICommandHandler<ChosenCommand, int>>());
        }
    }
}

namespace ElGuerre.Tendero.SharedKernel.Tests.Slices.Chosen
{
    public sealed record ChosenCommand : ICommand<int>;

    public sealed class ChosenHandler : ICommandHandler<ChosenCommand, int>
    {
        public Task<int> HandleAsync(ChosenCommand command, CancellationToken cancellationToken) => Task.FromResult(1);
    }

    public sealed class ChosenValidator : AbstractValidator<ChosenCommand>;
}

namespace ElGuerre.Tendero.SharedKernel.Tests.Slices.Other
{
    public sealed record OtherCommand : ICommand<int>;

    public sealed class OtherHandler : ICommandHandler<OtherCommand, int>
    {
        public Task<int> HandleAsync(OtherCommand command, CancellationToken cancellationToken) => Task.FromResult(2);
    }
}
