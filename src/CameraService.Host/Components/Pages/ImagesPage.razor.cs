namespace CameraService.Host.Components.Pages;

using CameraService.Core;
using CameraService.Host.Services;

using Microsoft.AspNetCore.Components;

// Saved images of a camera: a date (newest first), the hours of the date, and the thumbnails of an hour (loaded when they are shown).
// The viewer goes through all the images of the date.
public sealed partial class ImagesPage
{
    private const int HoursPerDay = 24;

    private readonly int[] hourCounts = new int[HoursPerDay];

    private Camera? camera;

    private IReadOnlyList<DateOnly> dates = [];

    private DateOnly selectedDate;

    private IReadOnlyList<TimeOnly> times = [];

    private int hour;

    private int? viewerIndex;

    private bool viewerPlay;

    //--------------------------------------------------------------------------------
    // Property
    //--------------------------------------------------------------------------------

    [Inject]
    public required CameraManager Manager { get; set; }

    [Inject]
    public required ImageStore Store { get; set; }

    [Inject]
    public required NavigationManager Navigation { get; set; }

    [Parameter]
    public string Id { get; set; } = string.Empty;

    // yyyyMMdd, the newest date when it is not given
    [Parameter]
    public string? Date { get; set; }

    //--------------------------------------------------------------------------------
    // Lifecycle
    //--------------------------------------------------------------------------------

    protected override void OnParametersSet()
    {
        camera = Manager.Find(Id);
        viewerIndex = null;
        Load(true);
    }

    //--------------------------------------------------------------------------------
    // Event
    //--------------------------------------------------------------------------------

    private void OnDateChanged(ChangeEventArgs e)
    {
        if ((camera is not null) && SnapshotStorage.TryParseDate(e.Value?.ToString() ?? string.Empty, out var date))
        {
            Navigation.NavigateTo(Links.Images(camera.Id, date));
        }
    }

    // The images saved since the page was opened (the selected hour is kept)
    private void Reload() => Load(false);

    // From the first image of the selected hour
    private void PlayHour()
    {
        var index = 0;
        while ((index < times.Count - 1) && (times[index].Hour != hour))
        {
            index++;
        }

        Open(index, true);
    }

    private void Open(int index, bool play)
    {
        viewerIndex = index;
        viewerPlay = play;
    }

    private void CloseViewer() => viewerIndex = null;

    //--------------------------------------------------------------------------------
    // Helper
    //--------------------------------------------------------------------------------

    private static string DateValue(DateOnly date) => SnapshotStorage.FormatDate(date);

    private void Load(bool newestHour)
    {
        Array.Clear(hourCounts);
        if (camera is null)
        {
            dates = [];
            times = [];
            return;
        }

        dates = Store.GetDates(camera);
        DateOnly? date = (Date is not null) && SnapshotStorage.TryParseDate(Date, out var parsed) ? parsed : dates.Count > 0 ? dates[0] : null;
        selectedDate = date ?? default;
        times = date is { } day ? Store.GetTimes(camera, day) : [];
        foreach (var time in times)
        {
            hourCounts[time.Hour]++;
        }

        if ((newestHour || (hourCounts[hour] == 0)) && (times.Count > 0))
        {
            hour = times[^1].Hour;
        }
    }
}
