using TaleWorlds.SaveSystem;

namespace ReignBeta.Family
{
    public sealed class ReignConceptionRecord
    {
        [SaveableField(1)] public string ConceptionId = string.Empty;
        [SaveableField(2)] public string MotherId = string.Empty;
        [SaveableField(3)] public string BiologicalFatherId = string.Empty;
        [SaveableField(4)] public string LegalFatherId = string.Empty;
        [SaveableField(5)] public float ConceptionDay;
        [SaveableField(6)] public float DueDay;
        [SaveableField(7)] public string Status = "active";
        [SaveableField(8)] public float Secrecy;
        [SaveableField(9)] public string ChildId = string.Empty;
        [SaveableField(10)] public bool PlayerInvolved;
        [SaveableField(11)] public bool IsIllegitimate;
        [SaveableField(12)] public string SpouseAtConceptionId = string.Empty;
        [SaveableField(13)] public bool MarriageAtConceptionKnown;
        [SaveableField(14)] public bool MotherExposed;
        [SaveableField(15)] public bool FatherExposed;
        [SaveableField(16)] public string KnowledgeJson = "{}";
        [SaveableField(17)] public string ContextSource = string.Empty;
    }

    public sealed class ReignParentageRecord
    {
        [SaveableField(1)] public string ChildId = string.Empty;
        [SaveableField(2)] public string MotherId = string.Empty;
        [SaveableField(3)] public string BiologicalFatherId = string.Empty;
        [SaveableField(4)] public string LegalFatherId = string.Empty;
        [SaveableField(5)] public bool IsIllegitimate;
        [SaveableField(6)] public bool Revealed;
        [SaveableField(7)] public string BastardSurname = string.Empty;
        [SaveableField(8)] public string ConceptionId = string.Empty;
        [SaveableField(9)] public bool MotherExposed;
        [SaveableField(10)] public bool FatherExposed;
    }
}
