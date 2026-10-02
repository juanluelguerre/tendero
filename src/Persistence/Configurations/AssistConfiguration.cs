using ElGuerre.Tendero.Assist.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ElGuerre.Tendero.Persistence.Configurations;

/// <summary>Assist's one table, in its own schema like every context's (ADR 0052).</summary>
internal sealed class ActionProposalConfiguration : IEntityTypeConfiguration<ActionProposal>
{
    public void Configure(EntityTypeBuilder<ActionProposal> builder)
    {
        builder.ToTable("ActionProposals", "assist");

        builder.HasKey(proposal => proposal.Id);
        builder.Property(proposal => proposal.Id).ValueGeneratedNever();

        builder.Property(proposal => proposal.Command).HasMaxLength(100).IsRequired();

        // jsonb, as the audit's payload: the command and its snapshot are read
        // back field by field by the review.
        builder.Property(proposal => proposal.Payload).HasColumnType("jsonb").IsRequired();
        builder.Property(proposal => proposal.Before).HasColumnType("jsonb");

        builder.Property(proposal => proposal.Subject).HasMaxLength(200).IsRequired();
        builder.Property(proposal => proposal.Reason).HasMaxLength(2000).IsRequired();
        builder.Property(proposal => proposal.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(proposal => proposal.DecidedBy).HasMaxLength(200);
        builder.Property(proposal => proposal.Result).HasMaxLength(1000);
        builder.Property(proposal => proposal.ProposedAt).IsRequired();

        builder.ComplexProperty(proposal => proposal.Provenance, provenance => provenance.ToJson("provenance"));

        // The row's version, Postgres's own xmin: two approvals of one proposal
        // both read it Proposed, and only the first save stands. The second is a
        // concurrency failure the unit of work reports, and the approval that made
        // it is told the proposal is no longer waiting (final review, I-2).
        builder.Property(proposal => proposal.Version).HasColumnName("xmin").IsRowVersion();

        // What the Proposals list asks (S-4): one status, the newest first.
        builder.HasIndex(proposal => new { proposal.Status, proposal.ProposedAt }).IsDescending(false, true);

        builder.Ignore(proposal => proposal.DomainEvents);
    }
}
