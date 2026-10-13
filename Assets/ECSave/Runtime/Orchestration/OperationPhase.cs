namespace ECSave.Internal
{
    public enum OperationPhase : byte
    {
        None = 0,
        S0_Guard = 1,
        S1_AwaitSnapshot = 2,
        S2_Encode = 3,
        S3_Seal = 4,
        S4_Complete = 5,
        D0_Guard = 10,
        D1_StructureValidation = 11,
        D2_Checksum = 12,
        D3_Version = 13,
        D4_WorldId = 14,
        D5_SchemaMatch = 15,
        D6_Migration = 16,
        D7_EntityRefValidation = 17,
        D8_RecordValidation = 18,
        D9_AwaitApply = 19,
        P0_FinalCancel = 20,
        P1_Plan = 21,
        P2_Backup = 22,
        P3_Generate = 23,
        P4_StructuralChange = 24,
        P5_ValueWrite = 25,
        P6_ChunkComponent = 26,
        P7_Destroy = 27,
        P8_Commit = 28,
    }
}
