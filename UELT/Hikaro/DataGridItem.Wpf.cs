using System.Windows.Media;

namespace UELT.Hikaro;

// GUI-only part of DataGridItem (not linked into the CLI).
public partial class DataGridItem
{
    public Geometry StatusSymbol => _status switch
    {
        RowStatus.NeedsReview => (Geometry)App.Current.Resources["NeedsReviewIcon"],
        RowStatus.Approved => (Geometry)App.Current.Resources["ApprovedIcon"],
        _ => null
    };
}
