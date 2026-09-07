using System.Threading.Tasks;
using DawnCapture.Views;
using Windows.Graphics;

namespace DawnCapture.Helpers;

public static class RegionPickerHelper
{
    public static async Task<RectInt32?> PickRegionAsync()
    {
        var virtualBounds = ScreenBoundsHelper.GetVirtualScreenBounds();
        var picker = new RegionPickerWindow(virtualBounds);
        return await picker.PickAsync();
    }
}
