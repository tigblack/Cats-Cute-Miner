using System;

namespace CatMiners.Core.Outpost
{
    public enum NeutralAge { Unselected, Unknown, Under13, Age13To15, Age16OrOlder }
    [Serializable]
    public sealed class IntegrationPrivacy
    {
        public NeutralAge Age;
        public bool ChoicesMade;
        public bool RewardedStartupChoiceMade;
        public bool AppMetrica, Adjust, Meta, Ads;
        // Basic gameplay analytics is automatic; only an explicit opt-out disables it.
        // AppMetrica remains serialized for migration/legacy callers, not this policy.
        public bool AppMetricaDisabled;
        public bool AppMetricaEnabled => !AppMetricaDisabled;
        public bool Protected => Age != NeutralAge.Age16OrOlder;
        public bool CanChooseAnalyticsAge => Age == NeutralAge.Unselected || Age == NeutralAge.Unknown;
        public bool MayInitialize(bool choice) => !Protected && ChoicesMade && choice;
        public bool TryChooseAnalyticsAge(NeutralAge age)
        {
            if (!CanChooseAnalyticsAge || age == NeutralAge.Unselected || !Enum.IsDefined(typeof(NeutralAge), age)) return false;
            ChooseAge(age);
            return true;
        }
        public void ChooseAge(NeutralAge age)
        {
            Age = Enum.IsDefined(typeof(NeutralAge), age) && age != NeutralAge.Unselected ? age : NeutralAge.Unknown;
            ChoicesMade = Protected;
            AppMetrica = Adjust = Meta = Ads = false;
        }
    }
    [Serializable]
    public sealed class RewardedOffer
    {
        /// <summary>ItemId — плитка магазина; Gold — удвоение офлайна или золото бонуса; Bonus — вид бонуса за рекламу (ТЗ 10.6).</summary>
        public string Id, RunId, ItemId, Gold, Bonus;
        /// <summary>Эликсир или гемы бонуса (ТЗ 10.6).</summary>
        public long Amount;
        public long OfferedUtcMs;
        public bool Earned;
    }
}
