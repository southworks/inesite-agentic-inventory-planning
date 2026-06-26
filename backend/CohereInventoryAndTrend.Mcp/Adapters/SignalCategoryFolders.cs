namespace CohereInventoryAndTrend.Mcp.Adapters;

public static class SignalCategoryFolders
{
    public static string For(SignalCategory category) => category switch
    {
        SignalCategory.PosTransactions => "01_pos_transactions",
        SignalCategory.SupplierData => "02_supplier_data",
        SignalCategory.PromotionsPrice => "03_promotions_price",
        SignalCategory.Inventory => "04_inventory",
        SignalCategory.DataEntry => "05_data_entry",
        _ => throw new ArgumentOutOfRangeException(nameof(category), category, null)
    };
}
