namespace CohereInventoryAndTrend.Mcp.Adapters;

public static class SignalCategoryFolders
{
    public static string For(SignalCategory category) => category switch
    {
        SignalCategory.PosTransactions => "pos_transaction_batch",
        SignalCategory.SupplierData => "supplier_profile",
        SignalCategory.PromotionsPrice => "promotion_event",
        SignalCategory.Inventory => "inventory_snapshot",
        SignalCategory.DataEntry => "data_entry",
        _ => throw new ArgumentOutOfRangeException(nameof(category), category, null)
    };

    public static string CategoryName(SignalCategory category) => For(category);
}
