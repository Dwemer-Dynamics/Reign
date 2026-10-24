using Reign.Core.Contracts.Actions;
using Reign.Core.Contracts.Government;
using Xunit;

namespace Reign.Mcp.Tests.Features.Government;

public sealed class GovernmentAgreementTimingTests
{
    [Theory]
    [InlineData(91196.75031559143, 6.53960856178, 0, 91200.5, false)]
    [InlineData(91196.75, 3, 0, 91200.5, true)]
    [InlineData(100, 2, 0, 102, false)]
    [InlineData(100, 0, 102, 102, false)]
    [InlineData(100, 0, 101, 102, true)]
    [InlineData(102, 0, 0, 102, true)]
    [InlineData(102.01, 0, 0, 102, false)]
    [InlineData(100, double.NaN, 0, 102, false)]
    [InlineData(100, 0, double.PositiveInfinity, 102, false)]
    [InlineData(100, -1, 0, 102, false)]
    public void A_vote_service_must_start_before_its_exact_boundary(double now, double delivery, double due, double vote, bool expected)
        => Assert.Equal(expected, GovernmentSessionRules.CanStartVoteService(now, delivery, due, vote));

    [Fact]
    public void Caucus_interval_dates_match_the_fifteenth_and_thirtieth_session_calendar()
    {
        for (int interval = 1; interval <= 8; interval++)
        {
            double day = GovernmentSessionRules.SessionCalendarDay(interval);
            Assert.Equal(interval % 2 == 1 ? 14d : 29d, day - GovernmentSessionRules.SeasonStart(day));
            Assert.Equal(day, GovernmentSessionRules.NextSession(day - .01));
        }
    }

    [Fact]
    public void An_infeasible_paid_vote_is_rejected_before_any_capture_or_custody()
    {
        var agreement = new ActionAgreement { Id = "late-vote", PlayerId = "player", NpcId = "member",
            State = AgreementState.Accepted, Origin = ActionOrigin.Console,
            Clauses = new List<ActionClause> {
                new() { Id = "vote", ActionId = "government.support_nomination", ActorId = "member", RecipientId = "player" },
                new() { Id = "fee", ActionId = "give.gold", ActorId = "player", RecipientId = "member",
                    Arguments = new SortedDictionary<string, string> { ["quantity"] = "3000" } } } };
        var journal = new AgreementJournal { Agreement = agreement };
        var backend = new LateVoteBackend();
        Assert.Throws<InvalidOperationException>(() => ReignAgreementEngine.Commit(journal, backend, 91196.75031559143, true));
        Assert.Equal(0, backend.Mutations);
        Assert.Empty(journal.Steps);
        Assert.Empty(journal.CommittedHash);
    }

    private sealed class LateVoteBackend : IAgreementBackend
    {
        public int Mutations;
        public IReadOnlyList<string> Preflight(ActionAgreement a) => GovernmentSessionRules.CanStartVoteService(91196.75031559143, 6.53960856178, 0, 91200.5)
            ? Array.Empty<string>() : new[] { "The agreement arrives after the selection." };
        public string Capture(ActionAgreement a, ActionClause c) { Mutations++; return ""; }
        public void Reserve(ActionAgreement a, ActionClause c, string s) => Mutations++;
        public void Return(ActionAgreement a, ActionClause c, string s, int n) => Mutations++;
        public void Apply(ActionAgreement a, ActionClause c, string s, int n) => Mutations++;
        public void ApplyAssetsAtomically(ActionAgreement a, IReadOnlyList<AgreementStep> steps) => Mutations++;
        public bool Observe(ActionAgreement a, ActionClause c, string s, int n) => false;
        public bool ConditionSatisfied(ActionClause c, double now) => true;
        public double DeliveryDays(ActionAgreement a) => 6.53960856178;
        public void SaveJournal(AgreementJournal j) => Mutations++;
    }
}
