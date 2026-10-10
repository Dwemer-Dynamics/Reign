using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Reign.Core.Contracts.Actions;
using ReignBeta.Actions;
using ReignBeta.Integration;
#if !REIGN_EXCLUDE_COURT
using ReignBeta.Court;
#endif
using TaleWorlds.CampaignSystem;
using TaleWorlds.Library;

namespace ReignBeta.UI.ViewModels
{
    public sealed class ReignActionChoiceVM : ViewModel
    {
        private readonly Action _select;
        public ReignActionChoiceVM(string label, string detail, bool enabled, Action select) { Label = label; Detail = detail; Enabled = enabled; _select = select; }
        [DataSourceProperty] public string Label { get; }
        [DataSourceProperty] public string Detail { get; }
        [DataSourceProperty] public bool Enabled { get; }
        [DataSourceProperty] public bool Disabled => !Enabled;
        public void ExecuteSelect() { if (Enabled) _select(); }
    }

    public sealed class ReignActionBuilderVM : ViewModel
    {
        private readonly IReadOnlyList<Hero> _participants;
        private readonly Action<ActionAgreement> _accept;
        private readonly Action _cancel;
        private readonly Stack<Action> _back = new Stack<Action>();
        private ActionAgreement _agreement;
        private ActionClause _clause;
        private Action _renderStep;
        private bool _playerActs;
        private string _search = "", _entry = "1", _title = "Begin your sentence", _error = "", _category = "";
        private bool _entryVisible, _review;
        private int _parameterIndex;
        private int _editIndex = -1;
        private Action<string> _entryAccept;
        private bool _finalized;
        private List<ActionParameter> _parameters;
        public ReignActionBuilderVM(IReadOnlyList<Hero> participants, Action<ActionAgreement> accept, Action cancel, ActionAgreement existing = null)
        {
            _participants = participants; _accept = accept; _cancel = cancel;
            _agreement = existing == null ? new ActionAgreement { PlayerId = Hero.MainHero.StringId, ProposerId = Hero.MainHero.StringId, Origin = ActionOrigin.Conversation, CreatedDay = ReignActionService.Now } : ReignActionService.Copy(existing);
            Choices = new MBBindingList<ReignActionChoiceVM>();
            if (_agreement.Clauses.Count > 0) ShowReview(); else ShowOpening();
        }
        [DataSourceProperty] public MBBindingList<ReignActionChoiceVM> Choices { get; }
        [DataSourceProperty] public string Title => _title;
        [DataSourceProperty] public string Recipient => string.IsNullOrEmpty(_agreement.NpcId) ? "Choose your words" : "To " + ReignActionService.HeroById(_agreement.NpcId)?.Name;
        [DataSourceProperty] public string Sentence
        {
            get
            {
                if (_clause != null && !_review)
                    return (_agreement.Clauses.Count == 0 ? "" : _agreement.Sentence + " ")
                        + ReignActionLanguage.Subject(_playerActs, _agreement.Clauses.Count) + ReignActionLanguage.Verb(ReignActionCatalog.Find(_clause.ActionId), _playerActs)
                        + " " + string.Join(" ", _labels.Values);
                return _agreement.Clauses.Count == 0 ? "Your action sentence will appear here." : _agreement.Sentence;
            }
        }
        [DataSourceProperty] public string Error => _error;
        [DataSourceProperty] public string Search { get => _search; set { _search = value ?? ""; OnPropertyChangedWithValue(_search); _renderStep?.Invoke(); } }
        [DataSourceProperty] public string Entry { get => _entry; set { _entry = value ?? ""; OnPropertyChangedWithValue(_entry); } }
        [DataSourceProperty] public bool EntryVisible => _entryVisible;
        [DataSourceProperty] public bool CanAccept => _review && _agreement.Clauses.Count > 0;
        [DataSourceProperty] public bool CanBack => _back.Count > 0;
        [DataSourceProperty] public bool SearchVisible => !_entryVisible && !_review;
        private void Refresh()
        {
            foreach (string property in new[] { nameof(Title), nameof(Recipient), nameof(Sentence), nameof(Error), nameof(EntryVisible), nameof(CanAccept), nameof(CanBack), nameof(SearchVisible) }) OnPropertyChanged(property);
        }
        private void Step(string title, Action render, bool entry = false, bool review = false)
        {
            _title = title; _entryVisible = entry; _review = review; _search = ""; _error = "";
            _entryAccept = null;
            OnPropertyChanged(nameof(Search)); _renderStep = render; render(); Refresh();
        }
        private void Choice(string label, Action select, bool enabled = true, string detail = "")
        {
            if (_search.Length > 0 && label.IndexOf(_search, StringComparison.CurrentCultureIgnoreCase) < 0 && detail.IndexOf(_search, StringComparison.CurrentCultureIgnoreCase) < 0) return;
            Choices.Add(new ReignActionChoiceVM(label, detail, enabled, select));
        }
        private void Forward(Action next, Action back)
        {
            // Each history entry owns its draft and parameter context. Later clauses cannot
            // overwrite the state captured by a previous Back button.
            var agreement = ReignActionService.Copy(_agreement);
            var clause = _clause == null ? null : Newtonsoft.Json.JsonConvert.DeserializeObject<ActionClause>(Newtonsoft.Json.JsonConvert.SerializeObject(_clause));
            var parameters = _parameters?.ToList();
            var labels = new Dictionary<string, string>(_labels);
            int parameterIndex = _parameterIndex;
            int editIndex = _editIndex;
            string entry = _entry, category = _category;
            bool playerActs = _playerActs;
            _back.Push(() =>
            {
                _agreement = agreement; _clause = clause; _parameters = parameters; _parameterIndex = parameterIndex;
                _entry = entry; _category = category; _playerActs = playerActs;
                _editIndex = editIndex;
                _labels.Clear(); foreach (var kv in labels) _labels[kv.Key] = kv.Value;
                back();
            });
            next();
        }
        private void ShowOpening() => Step("Begin your sentence", () =>
        {
            Choices.Clear();
            Choice("I would like you…", () => { _playerActs = false; Forward(ChoosePerson, ShowOpening); });
            Choice("I wish to…", () => { _playerActs = true; Forward(ChoosePerson, ShowOpening); });
            foreach (var pending in ReignAgreementBehavior.Instance?.Offers.Where(a => a.State == AgreementState.Counteroffered
                && a.PlayerId == Hero.MainHero.StringId && _participants.Any(h => h.StringId == a.NpcId)) ?? Enumerable.Empty<ActionAgreement>())
                Choice("Review offer from " + ReignActionService.HeroById(pending.NpcId)?.Name,
                    () => Forward(() => { _agreement = ReignActionService.Copy(pending); ShowReview(); }, ShowOpening), true, pending.Sentence);
        });
        private void ChoosePerson()
        {
            if (_participants.Count == 1) { _agreement.NpcId = _participants[0].StringId; ShowVerbs(); return; }
            Step("Who are you addressing?", () =>
            {
                Choices.Clear(); foreach (var hero in _participants.OrderBy(h => h.Name.ToString(), StringComparer.CurrentCultureIgnoreCase))
                    Choice(hero.Name.ToString(), () => { _agreement.NpcId = hero.StringId; Forward(ShowVerbs, ChoosePerson); });
            });
        }
        private void ShowVerbs() => Step("Choose an action", () =>
        {
            Choices.Clear();
            if (_search.Length > 0)
            { AddActionChoices(ReignActionCatalog.All.Where(d => d.ConversationEligible), ShowVerbs); return; }
            var available = ReignActionCatalog.All.Where(IsAvailable).ToList();
            if (available.Any(d => d.Id.StartsWith("give.", StringComparison.Ordinal)))
                Choice("Give…", () => Forward(ShowAssets, ShowVerbs), true, "Gold, possessions, troops, prisoners, land, workshops and ships");
            if (available.Any(d => d.Id == "go_to_settlement"))
                Choice("Go to…", () => Forward(() => BeginClause(ReignActionCatalog.Find("go_to_settlement")), ShowVerbs), true, "Castles, towns and villages");
            if (available.Any(d => SupportActions.Contains(d.Id)))
                Choice("Support…", () => Forward(ShowPolitics, ShowVerbs), true, "Political parties, proposals and a future rebellion");
            foreach (var category in new[] { new[] { "government", "Government decisions" }, new[] { "court", "Court and intelligence" },
                new[] { "campaign", "Party and military orders" }, new[] { "diplomacy", "Diplomacy" },
                new[] { "politics", "Clan and kingdom" }, new[] { "regular", "Personal requests" } })
                if (available.Any(d => ActionGroup(d) == category[0]))
                    Choice(category[1] + "…", () => Forward(() => ShowFamily(category[0], category[1]), ShowVerbs));
        });
        private static readonly string[] SupportActions = { "government.join_party", "government.back_platform", "government.support_vote", "pledge_rebellion_support" };
        private bool IsAvailable(ActionDefinition definition) => ReignActionService.CanBuild(definition, _agreement, _playerActs);
        private static string ActionGroup(ActionDefinition d)
        {
            if (d.NativeObjective.Length > 0 || d.Family == "strategy" || d.Id.EndsWith("campaign_order", StringComparison.Ordinal)) return "campaign";
            if (new[] { "hire_player_as_mercenary", "dismiss_player_mercenary", "offer_player_vassalage", "dismiss_player_vassal",
                "join_clan", "leave_clan", "join_kingdom", "leave_kingdom", "hire_mercenary_clan", "create_clan_accord", "cancel_clan_accord" }.Contains(d.Id)) return "politics";
            return d.Family;
        }
        private void ShowFamily(string family, string title) => Step(title, () =>
        {
            Choices.Clear(); AddActionChoices(ReignActionCatalog.All.Where(d => d.ConversationEligible && ActionGroup(d) == family), () => ShowFamily(family, title));
        });
        private void AddActionChoices(IEnumerable<ActionDefinition> definitions, Action back)
        {
            // Keep legacy aliases available to console tooling without showing the same
            // campaign objective twice in a player's selector.
            var actions = definitions.Where(IsAvailable).Where(d => !d.Id.StartsWith("campaign.", StringComparison.Ordinal)
                || !ReignActionCatalog.All.Any(alias => !alias.Id.StartsWith("campaign.", StringComparison.Ordinal)
                    && alias.ConversationEligible && alias.NativeObjective == d.NativeObjective));
            foreach (var d in actions.OrderBy(d => ReignActionLanguage.ChoiceLabel(d, _playerActs), StringComparer.CurrentCultureIgnoreCase))
            {
                Choice(ReignActionLanguage.ChoiceLabel(d, _playerActs), () => Forward(() => BeginClause(d), back));
            }
        }
        private void ShowAssets() => Step("What will be given?", () =>
        {
            Choices.Clear();
            foreach (var d in ReignActionCatalog.All.Where(d => d.Id.StartsWith("give.") && IsAvailable(d)).OrderBy(d => d.Label, StringComparer.CurrentCultureIgnoreCase))
                Choice(d.Label.Substring(5), () => Forward(() => BeginClause(d), ShowAssets));
        });
        private void ShowPolitics() => Step("What support are you proposing?", () =>
        {
            Choices.Clear(); AddActionChoices(ReignActionCatalog.All.Where(d => SupportActions.Contains(d.Id)), ShowPolitics);
        });
        private void BeginClause(ActionDefinition d)
        {
            if (!IsAvailable(d)) { ShowVerbs(); ShowError("This action is no longer available for the selected person."); return; }
            _editIndex = -1;
            _clause = new ActionClause { ActionId = d.Id, ActorId = _playerActs ? _agreement.PlayerId : _agreement.NpcId, RecipientId = _playerActs ? _agreement.NpcId : _agreement.PlayerId };
            bool boundRebellion = d.Id == "start_ruling_clan_rebellion" || d.Id == "pledge_rebellion_support";
            if (boundRebellion)
            {
                _clause.Arguments["actorKingdomId"] = Clan.PlayerClan?.Kingdom?.StringId ?? "";
                if (d.Id == "start_ruling_clan_rebellion") _clause.Arguments["actorClanId"] = Clan.PlayerClan?.StringId ?? "";
                else _clause.Arguments["terms.challengedRulerId"] = Clan.PlayerClan?.Kingdom?.Leader?.StringId ?? "";
            }
            _parameters = d.Parameters.Where(p => !p.Automatic && p.Key != "variantId" && p.Key != "equipmentSet" && p.Key != "equipmentSlot" && p.Key != "terms.purpose").ToList();
            if (boundRebellion) _parameters.Clear();
            _labels.Clear();
            _parameterIndex = 0; _category = ""; ShowParameter();
        }
        private void ShowParameter()
        {
            while (_parameterIndex < _parameters.Count && IsUnusedWageDetail(_parameters[_parameterIndex]))
            {
                _clause.Arguments.Remove(_parameters[_parameterIndex].Key);
                _labels.Remove(_parameters[_parameterIndex].Key);
                _parameterIndex++;
            }
            if (_parameterIndex >= _parameters.Count) { FinishClause(); return; }
            var p = _parameters[_parameterIndex];
            if (p.Choices.Length > 0)
            {
                Step("Select " + p.Label.ToLowerInvariant(), () => { Choices.Clear(); UnspecifiedChoice(p); foreach (string choice in p.Choices) Choice(ChoiceLabel(choice), () => Pick(p, choice, ChoiceLabel(choice))); });
                return;
            }
            if (p.Kind == "settlement" && _category.Length == 0)
            {
                Step("Choose a destination type", () =>
                {
                    Choices.Clear();
                    UnspecifiedChoice(p);
                    foreach (string kind in new[] { "castle", "town", "village" })
                        if (ReignActionEntities.Find(p.Kind, _clause.ActorId, category: kind, clause: _clause)
                            .Any(r => r.Available && ReignActionService.CanSelect(_clause, p, r.Id)))
                            Choice(char.ToUpperInvariant(kind[0]) + kind.Substring(1) + "s", () => { _category = kind; Forward(ShowParameter, () => { _category = ""; ShowParameter(); }); });
                }); return;
            }
            bool value = AgreementRules.IsNumericKind(p.Kind) || p.Kind == "text";
            if (value) { Entry = ReignActionService.Arg(_clause, p.Key, p.Kind == "quantity" ? "1" : ""); Step(p.Label, () => { Choices.Clear(); UnspecifiedChoice(p); }, true); return; }
            Step("Select " + p.Label.ToLowerInvariant(), () =>
            {
                Choices.Clear();
                UnspecifiedChoice(p);
                var rows = ReignActionEntities.Find(p.Kind, _clause.ActorId, _search, _category, _clause);
                foreach (var r in rows.Where(r => r.Available && ReignActionService.CanSelect(_clause, p, r.Id)))
                    Choice(r.Label, () => { if (p.Kind == "item") foreach (string key in new[] { "variantId", "equipmentSet", "equipmentSlot" }) _clause.Arguments.Remove(key); foreach (var kv in r.Arguments) _clause.Arguments[kv.Key] = kv.Value; Pick(p, r.Id, r.Label); }, r.Available,
                        r.Available ? r.Quantity > 0 ? r.Quantity + " available" : "" : r.Reason);
            });
#if !REIGN_EXCLUDE_COURT
            if (p.Kind == "spySocialItem" && ReignCourtCampaignBehavior.Instance != null)
                _ = LoadSocialChoicesAsync(_clause, _renderStep);
#endif
        }
        private void UnspecifiedChoice(ActionParameter parameter)
        {
            // This navigation option stays first even when the entity search has no matches.
            if (!parameter.Required)
                Choices.Add(new ReignActionChoiceVM("Leave unspecified", "", true, () => Pick(parameter, "", "")));
        }
        private bool IsUnusedWageDetail(ActionParameter parameter)
        {
            if (_clause.ActionId != "accept_temporary_party_guest" && _clause.ActionId != "renew_temporary_party_guest") return false;
            if (parameter.Key != "terms.wagePeriodDays" && parameter.Key != "terms.wageRecipientHeroStringId") return false;
            return !_clause.Arguments.TryGetValue("terms.wageGold", out var wage)
                || !decimal.TryParse(wage, NumberStyles.Number, CultureInfo.InvariantCulture, out var amount) || amount <= 0;
        }
#if !REIGN_EXCLUDE_COURT
        private async Task LoadSocialChoicesAsync(ActionClause clause, Action render)
        {
            string error = "";
            try { await ReignCourtCampaignBehavior.Instance.LoadAgreementSocialChoicesAsync(clause).ConfigureAwait(false); }
            catch (Exception ex) { error = ex.Message; }
            await ReignMainThread.InvokeAsync(() =>
            {
                if (_finalized || !ReferenceEquals(_clause, clause) || _renderStep != render) return;
                _error = error; render(); Refresh();
            }).ConfigureAwait(false);
        }
#endif
        public override void OnFinalize() { _finalized = true; base.OnFinalize(); }
        private readonly Dictionary<string, string> _labels = new Dictionary<string, string>();
        private static string ChoiceLabel(string value) => value == "true" ? "Yes" : value == "false" ? "No"
            : value.Length == 0 ? "" : char.ToUpperInvariant(value[0]) + value.Substring(1).Replace('_', ' ');
        private void Pick(ActionParameter p, string value, string label)
        {
            if (p.Key == "terms.wageGold" && decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var wage) && wage == 0)
                value = label = "";
            if (value.Length > 0) _clause.Arguments[p.Key] = value; else _clause.Arguments.Remove(p.Key);
            _labels[p.Key] = label; int previous = _parameterIndex; _parameterIndex++; _category = "";
            Forward(ShowParameter, () => { _parameterIndex = previous; ShowParameter(); });
        }
        public void ExecuteNext()
        {
            if (_entryVisible && _entryAccept != null) { _entryAccept(Entry.Trim()); return; }
            if (!_entryVisible || _parameterIndex >= _parameters.Count) return;
            var p = _parameters[_parameterIndex];
            string parameterError = AgreementRules.ValidateParameterValue(p, Entry.Trim());
            if (parameterError.Length > 0) { _error = parameterError; Refresh(); return; }
            Pick(p, Entry.Trim(), Entry.Trim());
        }
        private void FinishClause()
        {
            if (_editIndex >= 0) _agreement.Clauses[_editIndex] = _clause; else _agreement.Clauses.Add(_clause);
            _clause = null; _editIndex = -1;
            _agreement.Revise(); ReignActionWording.Render(_agreement);
            ShowReview();
        }
        private void ShowReview() => Step("Your sentence", () =>
        {
            Choices.Clear();
            if (AgreementRules.IsPlayerDeclaration(_agreement))
            {
                Choice("Declared when you send this message", () => { }, false, "The ruler cannot veto it. You can still go Back or Cancel.");
                return;
            }
            if (_agreement.NpcAcceptedHash == _agreement.TermsHash())
                Choice("Decline this offer", () => { ReignAgreementBehavior.Instance.DeclineOffer(_agreement.Id, _agreement.TermsHash()); _cancel(); });
            Choice(_playerActs ? "and I will give…" : "and you will give…", () => Forward(ShowAssets, ShowReview));
            Choice(_playerActs ? "and you will give…" : "and I will give…", () => { _playerActs = !_playerActs; Forward(ShowAssets, () => { _playerActs = !_playerActs; ShowReview(); }); });
            Choice("Add another action…", () => Forward(ShowVerbs, ShowReview));
            Choice("Edit a term or its timing…", () => Forward(ChooseTerm, ShowReview));
            if (_agreement.Clauses.Any(c => c.ActionId == "give.gold"))
                Choice(_agreement.AdvancePayment ? "Payment: in advance" : "Payment: after verified fulfillment", () =>
                {
                    _agreement.AdvancePayment = !_agreement.AdvancePayment;
                    _agreement.Revise(); ReignActionWording.Render(_agreement); ShowReview();
                });
        }, review: true);

        private void ChooseTerm() => Step("Choose a term to edit", () =>
        {
            Choices.Clear();
            for (int i = 0; i < _agreement.Clauses.Count; i++)
            {
                int index = i; var term = _agreement.Clauses[i];
                Choice((i + 1) + ". " + ReignActionLanguage.ChoiceLabel(ReignActionCatalog.Find(term.ActionId), term.ActorId == _agreement.PlayerId),
                    () => Forward(() => TermOptions(index), ChooseTerm), true, term.DisplayText);
            }
        });
        private void TermOptions(int index) => Step("Edit this term", () =>
        {
            Choices.Clear(); var term = _agreement.Clauses[index];
            if (term.ActionId != "pledge_rebellion_support") Choice("Change its selections…", () => Forward(() =>
            {
                _clause = Newtonsoft.Json.JsonConvert.DeserializeObject<ActionClause>(Newtonsoft.Json.JsonConvert.SerializeObject(term));
                _editIndex = index; _playerActs = term.ActorId == _agreement.PlayerId;
                _clause.Arguments.Remove("terms.purpose");
                _parameters = ReignActionCatalog.Find(term.ActionId).Parameters.Where(p => !p.Automatic && !new[] { "variantId", "equipmentSet", "equipmentSlot", "terms.purpose" }.Contains(p.Key)).ToList();
                _parameterIndex = 0; _labels.Clear(); _category = ""; ShowParameter();
            }, () => TermOptions(index)));
            Choice("Set when it happens…", () => Forward(() => TimingOptions(index), () => TermOptions(index)));
            if (new[] { "give.gold", "give.item", "give.troop", "give.prisoner" }.Contains(term.ActionId))
                Choice("Split into installments…", () => Forward(() => NumberEntry("Number of installments (1–365)", term.Installments, count =>
                {
                    if (count < 1 || count > 365 || count != Math.Floor(count)) { ShowError("Enter a whole number from 1 to 365."); return; }
                    if (count == 1) { term.Installments = 1; term.IntervalDays = 0; ChangedTerm(); return; }
                    NumberEntry("Days between installments", Math.Max(1, term.IntervalDays), interval =>
                    {
                        if (interval <= 0) { ShowError("The interval must be greater than zero."); return; }
                        term.Installments = (int)count; term.IntervalDays = interval; term.DueDay = Math.Max(term.DueDay, ReignActionService.Now); term.Timing = TermTiming.Installment; ChangedTerm();
                    });
                }), () => TermOptions(index)), true, "The displayed quantity is delivered in each installment.");
            Choice("Remove this term", () => { _agreement.Clauses.RemoveAt(index); _agreement.Revise(); ReignActionWording.Render(_agreement); _back.Clear(); if (_agreement.Clauses.Count == 0) ShowOpening(); else ShowReview(); });
        });
        private void TimingOptions(int index) => Step("When should this happen?", () =>
        {
            Choices.Clear(); var term = _agreement.Clauses[index];
            Choice("As soon as delivery and the bargain allow", () => { term.DueDay = 0; term.ConditionKind = ""; term.ConditionTargetId = ""; term.Timing = TermTiming.Immediate; ChangedTerm(); });
            Choice("After a number of days…", () => Forward(() => NumberEntry("Days from now", 1, days =>
            {
                if (days < 0 || days > 3650) { ShowError("Enter between zero and 3,650 days."); return; }
                term.DueDay = ReignActionService.Now + days; term.ConditionKind = "day"; term.ConditionTargetId = ""; term.Timing = TermTiming.OnCondition; ChangedTerm();
            }), () => TimingOptions(index)));
            foreach (var option in new[] { new[] { "arrival", "Arrival at…", "settlement" }, new[] { "release", "Release of prisoner…", "hero" },
                new[] { "ownership", "Ownership of…", "settlement" }, new[] { "peace", "Peace with…", "kingdom" }, new[] { "war", "War with…", "kingdom" } })
                Choice(option[1], () => Forward(() => Step(option[1], () =>
                {
                    Choices.Clear(); foreach (var target in ReignActionEntities.Find(option[2], term.ActorId, _search))
                        Choice(target.Label, () => { term.ConditionKind = option[0]; term.ConditionTargetId = target.Id; term.Timing = TermTiming.OnCondition; ChangedTerm(); });
                }), () => TimingOptions(index)));
        });
        private void NumberEntry(string title, double initial, Action<double> accept)
        {
            Entry = initial.ToString("0.##", CultureInfo.InvariantCulture);
            Step(title, () => Choices.Clear(), true);
            _entryAccept = value =>
            {
                if (!double.TryParse(value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out double n) || double.IsNaN(n) || double.IsInfinity(n)) { ShowError("Enter a valid number."); return; }
                accept(n);
            };
        }
        private void ShowError(string text) { _error = text; Refresh(); }
        private void ChangedTerm() { _agreement.Revise(); ReignActionWording.Render(_agreement); _back.Clear(); ShowReview(); }
        public void ExecuteBack() { if (_back.Count > 0) _back.Pop()(); }
        public void ExecuteCancel() => _cancel();
        public void ExecuteAccept()
        {
            if (!CanAccept) return;
            var errors = ReignActionService.Preview(_agreement);
            if (errors.Count > 0) { _error = string.Join(" ", errors); Refresh(); return; }
            _agreement.State = AgreementState.Offered;
            _agreement.PlayerAcceptedHash = _agreement.TermsHash();
            _accept(ReignActionService.Copy(_agreement));
        }
    }
}
