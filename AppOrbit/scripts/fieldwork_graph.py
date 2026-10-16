#!/usr/bin/env python3
"""Builds graph/fieldwork.graph.json: a second, differently shaped app graph for App Orbit.

Fieldwork is a field-service app for technicians (Uno Platform, MVUX). It is written to exercise
what Orderly does not: a view model shared across two features, a view model with more members
than a card shows, a screen with six states and screens with none, a component with no uses,
a navigation cycle, a route entered from a command, long member names, an "observed" evidence
kind and two inferred links at different confidence.

Every source reference is found by searching the generated source text, so line numbers cannot
drift from the excerpts. Run from AppOrbit/:  python scripts/fieldwork_graph.py
"""
from __future__ import annotations

import json
from pathlib import Path

FILES: dict[str, dict] = {}
NODES: list[dict] = []
EDGES: list[dict] = []


def src(fid: str, path: str, language: str, text: str, start: int = 1) -> None:
    lines = text.strip("\n").split("\n")
    FILES[fid] = {"id": fid, "path": path, "language": language, "startLine": start, "lines": lines}


def ln(fid: str, needle: str) -> int:
    f = FILES[fid]
    hits = [i for i, line in enumerate(f["lines"]) if needle in line]
    assert len(hits) == 1, f"{fid}: {needle!r} matched {len(hits)} lines"
    return f["startLine"] + hits[0]


def ref(fid: str, needle: str, end: str | None = None) -> dict:
    r = {"file": fid, "line": ln(fid, needle)}
    if end is not None:
        r["endLine"] = ln(fid, end)
        assert r["endLine"] >= r["line"], (fid, needle, end)
    return r


def ev(kind: str = "declared", confidence: float = 1.0, source: dict | None = None, rationale: str | None = None) -> dict:
    e = {"kind": kind, "confidence": confidence}
    if source:
        e["source"] = {k: source[k] for k in ("file", "line")}
    if rationale:
        e["rationale"] = rationale
    return e


def node(nid: str, ntype: str, name: str, source: dict | None = None, evidence: dict | None = None,
         summary: str | None = None, properties: dict | None = None, preview: dict | None = None) -> str:
    n: dict = {"id": nid, "type": ntype, "name": name}
    if summary:
        n["summary"] = summary
    if source:
        n["source"] = source
    n["evidence"] = evidence or ev(source=source)
    if properties is not None:
        n["properties"] = properties
    if preview is not None:
        n["preview"] = preview
    NODES.append(n)
    return nid


def edge(a: str, relation: str, b: str, source: dict | None = None, label: str | None = None, evidence: dict | None = None) -> None:
    e: dict = {"from": a, "relation": relation, "to": b}
    if label:
        e["label"] = label
    e["evidence"] = evidence or ev(source=source)
    EDGES.append(e)


def R(x: float, y: float, w: float, h: float) -> dict:
    return {"x": x, "y": y, "w": w, "h": h}


# ------------------------------------------------------------------------------------------- sources

src("f.app", "Fieldwork/App.xaml.cs", "csharp", r'''
        private static void RegisterRoutes(IViewRegistry views, IRouteRegistry routes)
        {
            views.Register(
                new ViewMap(ViewModel: typeof(ShellViewModel)),
                new ViewMap<TodayPage, TodayViewModel>(),
                new ViewMap<JobPage, JobViewModel>(Data: new DataMap<Job>()),
                new ViewMap<MapPage, MapViewModel>(),
                new ViewMap<PhotosPage, PhotosViewModel>(Data: new DataMap<Job>()),
                new ViewMap<SignaturePage, JobViewModel>(),
                new ViewMap<OutboxPage, OutboxViewModel>(),
                new ViewMap<ProfilePage, ProfileViewModel>(),
                new ViewMap<SettingsPage, SettingsViewModel>()
            );

            routes.Register(
                new RouteMap("", View: views.FindByViewModel<ShellViewModel>(),
                    Nested:
                    [
                        new ("Today", View: views.FindByView<TodayPage>(), IsDefault: true),
                        new ("Job", View: views.FindByView<JobPage>()),
                        new ("Map", View: views.FindByView<MapPage>()),
                        new ("Photos", View: views.FindByView<PhotosPage>()),
                        new ("Signature", View: views.FindByView<SignaturePage>()),
                        new ("Outbox", View: views.FindByView<OutboxPage>()),
                        new ("Profile", View: views.FindByView<ProfilePage>()),
                        new ("Settings", View: views.FindByView<SettingsPage>()),
                    ]
                )
            );
        }
''', start=52)

src("f.today-xaml", "Fieldwork/Presentation/TodayPage.xaml", "xaml", r'''
<Page x:Class="Fieldwork.Presentation.TodayPage"
      xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
      xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
      xmlns:uen="using:Uno.Extensions.Navigation.UI"
      xmlns:mvux="using:Uno.Extensions.Reactive.UI"
      xmlns:controls="using:Fieldwork.Controls">
  <Grid RowDefinitions="Auto,Auto,*" Padding="16">
    <Button x:Name="AvatarButton" HorizontalAlignment="Left"
            uen:Navigation.Request="Profile" Content="&#xE77B;" />
    <TextBlock Text="Today" Margin="48,0,0,0" Style="{StaticResource TitleLarge}" />
    <Button x:Name="OutboxButton" HorizontalAlignment="Right"
            uen:Navigation.Request="Outbox"
            Content="{Binding PendingUploads, Converter={StaticResource PendingLabel}}" />
    <InfoBar x:Name="OfflineBanner" Grid.Row="1" Severity="Warning"
             IsOpen="{Binding IsOffline}"
             Message="{Binding LastSyncedLabel}" />
    <mvux:FeedView Grid.Row="2" x:Name="JobsFeed" Source="{Binding Jobs}"
                   RefreshCommand="{Binding Refresh}">
      <DataTemplate>
        <ListView x:Name="JobList" ItemsSource="{Binding Data}"
                  uen:Navigation.Request="Job">
          <ListView.ItemTemplate>
            <DataTemplate>
              <Grid ColumnDefinitions="*,Auto">
                <TextBlock Text="{Binding CustomerName}" />
                <controls:StatusChip Grid.Column="1" Status="{Binding Status}" />
              </Grid>
            </DataTemplate>
          </ListView.ItemTemplate>
        </ListView>
      </DataTemplate>
      <mvux:FeedView.ProgressTemplate>
        <DataTemplate><ProgressRing IsActive="True" /></DataTemplate>
      </mvux:FeedView.ProgressTemplate>
      <mvux:FeedView.NoneTemplate>
        <DataTemplate><TextBlock Text="No jobs today. Enjoy the quiet." /></DataTemplate>
      </mvux:FeedView.NoneTemplate>
      <mvux:FeedView.ErrorTemplate>
        <DataTemplate><InfoBar Severity="Error" IsOpen="True" Message="Couldn't load today's jobs" /></DataTemplate>
      </mvux:FeedView.ErrorTemplate>
    </mvux:FeedView>
  </Grid>
</Page>
''')

src("f.today-vm", "Fieldwork/Presentation/TodayModel.cs", "csharp", r'''
namespace Fieldwork.Presentation;

public partial record TodayModel(IJobService Jobs, IConnectivity Network, IOutboxStore Outbox)
{
    public IListFeed<JobSummary> Jobs => ListFeed.Async(Jobs.TodayAsync);

    public IFeed<bool> IsOffline => Network.IsOffline;

    public IFeed<string> LastSyncedLabel => Outbox.LastSyncedAt.Select(t => $"Offline. Last synced {t:t}.");

    public IFeed<int> PendingUploads => Outbox.Pending.Select(p => p.Count);

    public async ValueTask Refresh(CancellationToken ct) => await Jobs.RefreshAsync(ct);
}
''')

src("f.job-xaml", "Fieldwork/Presentation/JobPage.xaml", "xaml", r'''
<Page x:Class="Fieldwork.Presentation.JobPage"
      xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
      xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
      xmlns:uen="using:Uno.Extensions.Navigation.UI"
      xmlns:mvux="using:Uno.Extensions.Reactive.UI"
      xmlns:controls="using:Fieldwork.Controls">
  <mvux:FeedView x:Name="JobFeed" Source="{Binding Job}">
    <DataTemplate>
      <Grid RowDefinitions="Auto,Auto,Auto,Auto,*,Auto" Padding="16" RowSpacing="12">
        <TextBlock x:Name="CustomerText" Text="{Binding Data.CustomerName}" Style="{StaticResource TitleLarge}" />
        <controls:StatusChip x:Name="StatusHeader" HorizontalAlignment="Right"
                             Status="{Binding Status}" />
        <controls:StatusChip x:Name="SyncChip" Grid.Row="1" VerticalAlignment="Bottom"
                             Status="{Binding HasUnsyncedChangesSinceLastSuccessfulUpload, Converter={StaticResource SyncStatus}}" />
        <TextBlock x:Name="AddressText" Grid.Row="1" Text="{Binding Data.SiteAddress}" />
        <Button x:Name="MapButton" Grid.Row="1" HorizontalAlignment="Right"
                uen:Navigation.Request="Map" Content="Map" />
        <TextBox x:Name="NotesBox" Grid.Row="2" Header="Notes" AcceptsReturn="True"
                 Text="{Binding Notes, Mode=TwoWay}" />
        <ItemsRepeater x:Name="PhotoStrip" Grid.Row="3" ItemsSource="{Binding Photos}">
          <ItemsRepeater.ItemTemplate>
            <DataTemplate><controls:PhotoTile Source="{Binding Thumbnail}" /></DataTemplate>
          </ItemsRepeater.ItemTemplate>
        </ItemsRepeater>
        <Button x:Name="PhotosButton" Grid.Row="4" VerticalAlignment="Top"
                uen:Navigation.Request="Photos" Content="{Binding PhotoCount, Converter={StaticResource PhotoLabel}}" />
        <Button x:Name="SignButton" Grid.Row="4" HorizontalAlignment="Right" VerticalAlignment="Top"
                uen:Navigation.Request="Signature" Content="Customer signature" />
        <Button x:Name="CompleteButton" Grid.Row="5" HorizontalAlignment="Stretch"
                Style="{StaticResource FilledButtonStyle}"
                IsEnabled="{Binding CanComplete}"
                Command="{Binding Complete}">
          <Grid>
            <TextBlock Text="Complete job" />
            <ProgressRing IsActive="{Binding IsCompleting}" />
          </Grid>
        </Button>
      </Grid>
    </DataTemplate>
    <mvux:FeedView.ProgressTemplate>
      <DataTemplate><ProgressRing IsActive="True" /></DataTemplate>
    </mvux:FeedView.ProgressTemplate>
    <mvux:FeedView.ErrorTemplate>
      <DataTemplate><InfoBar Severity="Error" IsOpen="True" Message="{Binding LastError}" /></DataTemplate>
    </mvux:FeedView.ErrorTemplate>
  </mvux:FeedView>
</Page>
''')

src("f.job-vm", "Fieldwork/Presentation/JobModel.cs", "csharp", r'''
namespace Fieldwork.Presentation;

/// Shared by JobPage and SignaturePage (two features, one model).
public partial record JobModel(Job Entity, IJobService Jobs, IOutboxStore Outbox, INavigator Navigator)
{
    public IFeed<Job> Job => Feed.Async(ct => Jobs.GetAsync(Entity.Id, ct));

    public IState<JobStatus> Status => State.Value(this, () => Entity.Status);

    public IState<string> Notes => State.Value(this, () => Entity.Notes ?? string.Empty);

    public IListFeed<Photo> Photos => ListFeed.Async(ct => Jobs.PhotosAsync(Entity.Id, ct));

    public IFeed<int> PhotoCount => Photos.AsFeed().Select(p => p.Count);

    public IState<byte[]?> Signature => State<byte[]?>.Empty(this);

    public IFeed<bool> HasSignature => Signature.Select(s => s is { Length: > 0 });

    public IState<bool> IsCompleting => State.Value(this, () => false);

    public IState<string?> LastError => State<string?>.Empty(this);

    public IFeed<bool> CanComplete => Feed.Combine(HasSignature, PhotoCount, Status, IsCompleting)
        .Select(t => t.Item1 && t.Item2 >= 1 && t.Item3 == JobStatus.OnSite && !t.Item4);

    public IFeed<bool> HasUnsyncedChangesSinceLastSuccessfulUpload => Outbox.HasPendingFor(Entity.Id);

    public IFeed<string> EstimatedTravelTimeFromCurrentLocation => Jobs.TravelEstimate(Entity.Id);

    public async ValueTask SaveSignature(byte[] strokes, CancellationToken ct) => await Signature.SetAsync(strokes, ct);

    public async ValueTask ClearSignature(CancellationToken ct) => await Signature.SetAsync(null, ct);

    public async ValueTask Complete(CancellationToken ct)
    {
        await IsCompleting.SetAsync(true, ct);
        try
        {
            await Jobs.CompleteAsync(Entity.Id, await Notes, await Signature, ct);
            await Navigator.NavigateRouteAsync(this, "Today", cancellation: ct);
        }
        catch (JobException ex)
        {
            await LastError.SetAsync(ex.Message, ct);
        }
        finally
        {
            await IsCompleting.SetAsync(false, ct);
        }
    }
}
''')

src("f.map-xaml", "Fieldwork/Presentation/MapPage.xaml", "xaml", r'''
<Page x:Class="Fieldwork.Presentation.MapPage"
      xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
      xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
      xmlns:uen="using:Uno.Extensions.Navigation.UI"
      xmlns:mvux="using:Uno.Extensions.Reactive.UI"
      xmlns:maps="using:Fieldwork.Maps">
  <Grid RowDefinitions="Auto,*">
    <TextBlock Text="Nearby jobs" Margin="16" Style="{StaticResource TitleLarge}" />
    <mvux:FeedView Grid.Row="1" x:Name="PinsFeed" Source="{Binding JobPins}">
      <DataTemplate>
        <maps:MapView x:Name="JobMap" Pins="{Binding Data}"
                      SelectedPin="{Binding SelectedJob, Mode=TwoWay}"
                      uen:Navigation.Request="Job" />
      </DataTemplate>
      <mvux:FeedView.ProgressTemplate>
        <DataTemplate><ProgressRing IsActive="True" /></DataTemplate>
      </mvux:FeedView.ProgressTemplate>
    </mvux:FeedView>
  </Grid>
</Page>
''')

src("f.map-vm", "Fieldwork/Presentation/MapModel.cs", "csharp", r'''
namespace Fieldwork.Presentation;

public partial record MapModel(IJobService Jobs, ILocationService Location)
{
    public IListFeed<JobPin> JobPins => ListFeed.Async(ct => Jobs.PinsNearAsync(Location.Current, ct));

    public IState<JobPin?> SelectedJob => State<JobPin?>.Empty(this);
}
''')

src("f.photos-xaml", "Fieldwork/Presentation/PhotosPage.xaml", "xaml", r'''
<Page x:Class="Fieldwork.Presentation.PhotosPage"
      xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
      xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
      xmlns:mvux="using:Uno.Extensions.Reactive.UI"
      xmlns:controls="using:Fieldwork.Controls">
  <Grid RowDefinitions="Auto,Auto,*,Auto" Padding="16" RowSpacing="12">
    <TextBlock Text="Photos" Style="{StaticResource TitleLarge}" />
    <InfoBar x:Name="CameraWarning" Grid.Row="1" Severity="Warning"
             IsOpen="{Binding CameraUnavailable}"
             Message="No camera found. Attach photos from files instead." />
    <mvux:FeedView Grid.Row="2" x:Name="PhotosFeed" Source="{Binding Photos}">
      <DataTemplate>
        <GridView x:Name="PhotoGrid" ItemsSource="{Binding Data}">
          <GridView.ItemTemplate>
            <DataTemplate><controls:PhotoTile Source="{Binding Thumbnail}" /></DataTemplate>
          </GridView.ItemTemplate>
        </GridView>
      </DataTemplate>
      <mvux:FeedView.NoneTemplate>
        <DataTemplate><TextBlock Text="No photos yet. Capture at least one before completing." /></DataTemplate>
      </mvux:FeedView.NoneTemplate>
    </mvux:FeedView>
    <Button x:Name="CaptureButton" Grid.Row="3" HorizontalAlignment="Stretch"
            Style="{StaticResource FilledButtonStyle}"
            IsEnabled="{Binding CanCapture}"
            Command="{Binding Capture}">
      <Grid>
        <TextBlock Text="Take photo" />
        <ProgressRing IsActive="{Binding IsCapturing}" />
      </Grid>
    </Button>
  </Grid>
</Page>
''')

src("f.photos-vm", "Fieldwork/Presentation/PhotosModel.cs", "csharp", r'''
namespace Fieldwork.Presentation;

public partial record PhotosModel(Job Entity, IJobService Jobs, ICameraService Camera)
{
    public IListState<Photo> Photos => ListState.Async(this, ct => Jobs.PhotosAsync(Entity.Id, ct));

    public IState<bool> IsCapturing => State.Value(this, () => false);

    public IFeed<bool> CameraUnavailable => Camera.IsAvailable.Select(a => !a);

    public IFeed<bool> CanCapture => Feed.Combine(CameraUnavailable, IsCapturing).Select(t => !t.Item1 && !t.Item2);

    public async ValueTask Capture(CancellationToken ct)
    {
        await IsCapturing.SetAsync(true, ct);
        try { await Photos.AddAsync(await Camera.CaptureAsync(ct), ct); }
        finally { await IsCapturing.SetAsync(false, ct); }
    }
}
''')

src("f.signature-xaml", "Fieldwork/Presentation/SignaturePage.xaml", "xaml", r'''
<Page x:Class="Fieldwork.Presentation.SignaturePage"
      xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
      xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
      xmlns:uen="using:Uno.Extensions.Navigation.UI"
      xmlns:ink="using:Fieldwork.Ink">
  <Grid RowDefinitions="Auto,*,Auto" Padding="16" RowSpacing="12">
    <TextBlock Text="Customer signature" Style="{StaticResource TitleLarge}" />
    <ink:SignaturePad x:Name="Pad" Grid.Row="1"
                      Strokes="{Binding Signature, Mode=TwoWay}" />
    <StackPanel Grid.Row="2" Orientation="Horizontal" Spacing="8" HorizontalAlignment="Right">
      <Button x:Name="ClearButton" Content="Clear" Command="{Binding ClearSignature}" />
      <Button x:Name="DoneButton" Content="Done" Style="{StaticResource FilledButtonStyle}"
              IsEnabled="{Binding HasSignature}"
              uen:Navigation.Request="-" />
    </StackPanel>
  </Grid>
</Page>
''')

src("f.outbox-xaml", "Fieldwork/Presentation/OutboxPage.xaml", "xaml", r'''
<Page x:Class="Fieldwork.Presentation.OutboxPage"
      xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
      xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
      xmlns:mvux="using:Uno.Extensions.Reactive.UI"
      xmlns:controls="using:Fieldwork.Controls">
  <Grid RowDefinitions="Auto,Auto,*,Auto" Padding="16" RowSpacing="12">
    <TextBlock Text="Outbox" Style="{StaticResource TitleLarge}" />
    <TextBlock x:Name="FailedText" Grid.Row="1"
               Text="{Binding FailedCount, Converter={StaticResource FailedLabel}}" />
    <mvux:FeedView Grid.Row="2" x:Name="OutboxFeed" Source="{Binding Items}">
      <DataTemplate>
        <ListView x:Name="OutboxList" ItemsSource="{Binding Data}" SelectionMode="None">
          <ListView.ItemTemplate>
            <DataTemplate>
              <Grid ColumnDefinitions="*,Auto">
                <TextBlock Text="{Binding Title}" />
                <controls:StatusChip Grid.Column="1" Status="{Binding State}" />
              </Grid>
            </DataTemplate>
          </ListView.ItemTemplate>
        </ListView>
      </DataTemplate>
      <mvux:FeedView.NoneTemplate>
        <DataTemplate><TextBlock Text="Everything is synced." /></DataTemplate>
      </mvux:FeedView.NoneTemplate>
    </mvux:FeedView>
    <Button x:Name="SyncNowButton" Grid.Row="3" HorizontalAlignment="Stretch"
            Style="{StaticResource FilledButtonStyle}"
            IsEnabled="{Binding CanSync}"
            Command="{Binding SyncNow}">
      <Grid>
        <TextBlock Text="Sync now" />
        <ProgressRing IsActive="{Binding IsSyncing}" />
      </Grid>
    </Button>
  </Grid>
</Page>
''')

src("f.outbox-vm", "Fieldwork/Presentation/OutboxModel.cs", "csharp", r'''
namespace Fieldwork.Presentation;

public partial record OutboxModel(IOutboxStore Outbox, ISyncService Sync, IConnectivity Network)
{
    public IListFeed<OutboxItem> Items => Outbox.Pending.AsListFeed();

    public IFeed<int> FailedCount => Outbox.Pending.Select(p => p.Count(i => i.State == SyncState.Failed));

    public IState<bool> IsSyncing => State.Value(this, () => false);

    public IFeed<bool> CanSync => Feed.Combine(Network.IsOffline, IsSyncing).Select(t => !t.Item1 && !t.Item2);

    public async ValueTask SyncNow(CancellationToken ct)
    {
        await IsSyncing.SetAsync(true, ct);
        try { await Sync.PushAsync(ct); }
        finally { await IsSyncing.SetAsync(false, ct); }
    }
}
''')

src("f.profile-xaml", "Fieldwork/Presentation/ProfilePage.xaml", "xaml", r'''
<Page x:Class="Fieldwork.Presentation.ProfilePage"
      xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
      xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
      xmlns:uen="using:Uno.Extensions.Navigation.UI">
  <StackPanel Padding="16" Spacing="12">
    <TextBlock x:Name="NameText" Text="{Binding DisplayName}" Style="{StaticResource TitleLarge}" />
    <TextBlock x:Name="RegionText" Text="{Binding Region}" />
    <Button x:Name="SettingsButton" Content="Settings" uen:Navigation.Request="Settings" />
    <Button x:Name="SignOutButton" Content="Sign out" Command="{Binding SignOut}" />
  </StackPanel>
</Page>
''')

src("f.profile-vm", "Fieldwork/Presentation/ProfileModel.cs", "csharp", r'''
namespace Fieldwork.Presentation;

public partial record ProfileModel(ITechnicianService Technicians, IAuthenticationService Auth)
{
    public IFeed<string> DisplayName => Technicians.Current.Select(t => t.DisplayName);

    public IFeed<string> Region => Technicians.Current.Select(t => t.Region);

    public async ValueTask SignOut(CancellationToken ct) => await Auth.LogoutAsync(ct);
}
''')

src("f.settings-xaml", "Fieldwork/Presentation/SettingsPage.xaml", "xaml", r'''
<Page x:Class="Fieldwork.Presentation.SettingsPage"
      xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
      xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
  <StackPanel Padding="16" Spacing="12">
    <TextBlock Text="Settings" Style="{StaticResource TitleLarge}" />
    <ToggleSwitch x:Name="WifiOnlyToggle" Header="Upload photos on Wi-Fi only"
                  IsOn="{Binding UploadOnWifiOnly, Mode=TwoWay}" />
    <ComboBox x:Name="MapStyleBox" Header="Map style"
              ItemsSource="{Binding MapStyles}" SelectedItem="{Binding MapStyle, Mode=TwoWay}" />
  </StackPanel>
</Page>
''')

src("f.settings-vm", "Fieldwork/Presentation/SettingsModel.cs", "csharp", r'''
namespace Fieldwork.Presentation;

public partial record SettingsModel(IWritableOptions<FieldworkSettings> Options)
{
    public IState<bool> UploadOnWifiOnly => State.Value(this, () => Options.Value.UploadOnWifiOnly);

    public IListFeed<string> MapStyles => ListFeed.Async(_ => ValueTask.FromResult<IImmutableList<string>>(["Streets", "Satellite"]));

    public IState<string> MapStyle => State.Value(this, () => Options.Value.MapStyle);
}
''')

src("f.status-chip", "Fieldwork/Controls/StatusChip.xaml", "xaml", r'''
<UserControl x:Class="Fieldwork.Controls.StatusChip"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
  <Border CornerRadius="10" Padding="8,2" Background="{x:Bind Status, Converter={StaticResource StatusBrush}, Mode=OneWay}">
    <TextBlock Text="{x:Bind Status, Mode=OneWay}" Style="{StaticResource LabelSmall}" />
  </Border>
</UserControl>
''')

src("f.photo-tile", "Fieldwork/Controls/PhotoTile.xaml", "xaml", r'''
<UserControl x:Class="Fieldwork.Controls.PhotoTile"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
  <Grid Width="96" Height="96" CornerRadius="8">
    <Image Source="{x:Bind Source, Mode=OneWay}" Stretch="UniformToFill" />
    <FontIcon x:Name="PendingIcon" Glyph="&#xE898;" Visibility="{x:Bind IsPending, Mode=OneWay}" />
  </Grid>
</UserControl>
''')

src("f.legacy-banner", "Fieldwork/Controls/LegacyBanner.xaml", "xaml", r'''
<UserControl x:Class="Fieldwork.Controls.LegacyBanner"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
  <!-- Kept for the 1.x migration notice. No page references it since 2.0. -->
  <InfoBar Severity="Informational" IsOpen="True" Message="Fieldwork has moved to the new sync engine." />
</UserControl>
''')

# ------------------------------------------------------------------------------------------- features

F_JOBS = node("feature.jobs", "feature", "Jobs", summary="See today's work, open a job, find it on the map.",
              evidence=ev("derived", 0.9, rationale="Today, Job and Map share the Job data map and navigate among themselves."))
F_CAPTURE = node("feature.capture", "feature", "Capture", summary="Collect proof of work: photos and the customer's signature.",
                 evidence=ev("derived", 0.85, rationale="Photos and Signature are reached only from Job and both write evidence for completion."))
F_SYNC = node("feature.sync", "feature", "Sync", summary="Work that is waiting to upload.",
              evidence=ev("derived", 0.9, rationale="OutboxPage is the only page bound to the sync service."))
F_ACCOUNT = node("feature.account", "feature", "Account", summary="Who is signed in, and device preferences.",
                 evidence=ev("derived", 0.8, rationale="Profile and Settings are reached from the avatar and touch no job data."))

# ------------------------------------------------------------------------------------------- view models and members

def viewmodel(nid: str, name: str, model: str, fid: str, needle: str, summary: str) -> str:
    return node(nid, "viewmodel", name, ref(fid, needle), summary=summary,
                properties={"uno": {"pattern": "MVUX", "model": model}})


def member(vm: str, nid: str, ntype: str, name: str, fid: str, needle: str, clr: str, end: str | None = None,
           summary: str | None = None, **facts) -> str:
    s = ref(fid, needle, end)
    props = {"clrType": clr, **facts}
    node(nid, ntype, name, s, summary=summary, properties=props)
    edge(vm, "exposes", nid, s)
    return nid


VM_TODAY = viewmodel("vm.today", "TodayViewModel", "TodayModel", "f.today-vm", "public partial record TodayModel", "Generated from TodayModel (MVUX).")
P_JOBS = member(VM_TODAY, "prop.today.jobs", "property", "Jobs", "f.today-vm", "IListFeed<JobSummary> Jobs", "IListFeed<JobSummary>")
P_OFFLINE = member(VM_TODAY, "prop.today.is-offline", "property", "IsOffline", "f.today-vm", "IFeed<bool> IsOffline", "IFeed<bool>", expression="Network.IsOffline")
P_SYNCED = member(VM_TODAY, "prop.today.last-synced-label", "property", "LastSyncedLabel", "f.today-vm", "LastSyncedLabel =>", "IFeed<string>")
P_PENDING = member(VM_TODAY, "prop.today.pending-uploads", "property", "PendingUploads", "f.today-vm", "PendingUploads =>", "IFeed<int>", expression="Outbox.Pending.Select(p => p.Count)")
C_REFRESH = member(VM_TODAY, "cmd.today.refresh", "command", "Refresh", "f.today-vm", "ValueTask Refresh", "ICommand (generated)", **{"async": True})

VM_JOB = viewmodel("vm.job", "JobViewModel", "JobModel", "f.job-vm", "public partial record JobModel", "Generated from JobModel (MVUX). Serves Job and Signature, across two features.")
P_JOB = member(VM_JOB, "prop.job.job", "property", "Job", "f.job-vm", "public IFeed<Job> Job =>", "IFeed<Job>")
P_STATUS = member(VM_JOB, "prop.job.status", "property", "Status", "f.job-vm", "IState<JobStatus> Status", "IState<JobStatus>", writable=True)
P_NOTES = member(VM_JOB, "prop.job.notes", "property", "Notes", "f.job-vm", "IState<string> Notes", "IState<string>", default='""', writable=True)
P_PHOTOS = member(VM_JOB, "prop.job.photos", "property", "Photos", "f.job-vm", "IListFeed<Photo> Photos", "IListFeed<Photo>")
P_PHOTO_COUNT = member(VM_JOB, "prop.job.photo-count", "property", "PhotoCount", "f.job-vm", "IFeed<int> PhotoCount", "IFeed<int>", expression="Photos.AsFeed().Select(p => p.Count)")
P_SIGNATURE = member(VM_JOB, "prop.job.signature", "property", "Signature", "f.job-vm", "IState<byte[]?> Signature", "IState<byte[]?>", writable=True)
P_HAS_SIG = member(VM_JOB, "prop.job.has-signature", "property", "HasSignature", "f.job-vm", "IFeed<bool> HasSignature", "IFeed<bool>", expression="Signature.Select(s => s is { Length: > 0 })")
P_COMPLETING = member(VM_JOB, "prop.job.is-completing", "property", "IsCompleting", "f.job-vm", "IState<bool> IsCompleting", "IState<bool>", default="false", writable=True)
P_JOB_ERROR = member(VM_JOB, "prop.job.last-error", "property", "LastError", "f.job-vm", "IState<string?> LastError", "IState<string?>", writable=True)
P_CAN_COMPLETE = member(VM_JOB, "prop.job.can-complete", "property", "CanComplete", "f.job-vm", "IFeed<bool> CanComplete", "IFeed<bool>",
                        end=".Select(t => t.Item1 && t.Item2 >= 1",
                        summary="True when there is a signature, at least one photo, the job is on site, and nothing is completing.",
                        expression="Feed.Combine(HasSignature, PhotoCount, Status, IsCompleting).Select(t => t.Item1 && t.Item2 >= 1 && t.Item3 == JobStatus.OnSite && !t.Item4)")
P_UNSYNCED = member(VM_JOB, "prop.job.has-unsynced-changes-since-last-successful-upload", "property",
                    "HasUnsyncedChangesSinceLastSuccessfulUpload", "f.job-vm", "HasUnsyncedChangesSinceLastSuccessfulUpload =>", "IFeed<bool>",
                    expression="Outbox.HasPendingFor(Entity.Id)")
P_TRAVEL = member(VM_JOB, "prop.job.estimated-travel-time-from-current-location", "property",
                  "EstimatedTravelTimeFromCurrentLocation", "f.job-vm", "EstimatedTravelTimeFromCurrentLocation =>", "IFeed<string>")
C_SAVE_SIG = member(VM_JOB, "cmd.job.save-signature", "command", "SaveSignature", "f.job-vm", "ValueTask SaveSignature", "ICommand (generated)", **{"async": True})
C_CLEAR_SIG = member(VM_JOB, "cmd.job.clear-signature", "command", "ClearSignature", "f.job-vm", "ValueTask ClearSignature", "ICommand (generated)", **{"async": True})
C_COMPLETE = member(VM_JOB, "cmd.job.complete", "command", "Complete", "f.job-vm", "ValueTask Complete", "ICommand (generated)",
                    end="await IsCompleting.SetAsync(false, ct);",
                    summary="Uploads notes and signature, then returns to Today. On JobException, sets LastError.", **{"async": True})

VM_MAP = viewmodel("vm.map", "MapViewModel", "MapModel", "f.map-vm", "public partial record MapModel", "Generated from MapModel (MVUX).")
P_PINS = member(VM_MAP, "prop.map.job-pins", "property", "JobPins", "f.map-vm", "IListFeed<JobPin> JobPins", "IListFeed<JobPin>")
P_SELECTED = member(VM_MAP, "prop.map.selected-job", "property", "SelectedJob", "f.map-vm", "IState<JobPin?> SelectedJob", "IState<JobPin?>", writable=True)

VM_PHOTOS = viewmodel("vm.photos", "PhotosViewModel", "PhotosModel", "f.photos-vm", "public partial record PhotosModel", "Generated from PhotosModel (MVUX).")
P_PHOTO_LIST = member(VM_PHOTOS, "prop.photos.photos", "property", "Photos", "f.photos-vm", "IListState<Photo> Photos", "IListState<Photo>", writable=True)
P_CAPTURING = member(VM_PHOTOS, "prop.photos.is-capturing", "property", "IsCapturing", "f.photos-vm", "IState<bool> IsCapturing", "IState<bool>", default="false", writable=True)
P_NO_CAMERA = member(VM_PHOTOS, "prop.photos.camera-unavailable", "property", "CameraUnavailable", "f.photos-vm", "IFeed<bool> CameraUnavailable", "IFeed<bool>", expression="Camera.IsAvailable.Select(a => !a)")
P_CAN_CAPTURE = member(VM_PHOTOS, "prop.photos.can-capture", "property", "CanCapture", "f.photos-vm", "IFeed<bool> CanCapture", "IFeed<bool>", expression="Feed.Combine(CameraUnavailable, IsCapturing).Select(t => !t.Item1 && !t.Item2)")
C_CAPTURE = member(VM_PHOTOS, "cmd.photos.capture", "command", "Capture", "f.photos-vm", "ValueTask Capture", "ICommand (generated)",
                   end="finally { await IsCapturing.SetAsync(false, ct); }", **{"async": True})

VM_OUTBOX = viewmodel("vm.outbox", "OutboxViewModel", "OutboxModel", "f.outbox-vm", "public partial record OutboxModel", "Generated from OutboxModel (MVUX).")
P_ITEMS = member(VM_OUTBOX, "prop.outbox.items", "property", "Items", "f.outbox-vm", "IListFeed<OutboxItem> Items", "IListFeed<OutboxItem>")
P_FAILED = member(VM_OUTBOX, "prop.outbox.failed-count", "property", "FailedCount", "f.outbox-vm", "IFeed<int> FailedCount", "IFeed<int>")
P_SYNCING = member(VM_OUTBOX, "prop.outbox.is-syncing", "property", "IsSyncing", "f.outbox-vm", "IState<bool> IsSyncing", "IState<bool>", default="false", writable=True)
P_CAN_SYNC = member(VM_OUTBOX, "prop.outbox.can-sync", "property", "CanSync", "f.outbox-vm", "IFeed<bool> CanSync", "IFeed<bool>", expression="Feed.Combine(Network.IsOffline, IsSyncing).Select(t => !t.Item1 && !t.Item2)")
C_SYNC = member(VM_OUTBOX, "cmd.outbox.sync-now", "command", "SyncNow", "f.outbox-vm", "ValueTask SyncNow", "ICommand (generated)",
                end="finally { await IsSyncing.SetAsync(false, ct); }", **{"async": True})

VM_PROFILE = viewmodel("vm.profile", "ProfileViewModel", "ProfileModel", "f.profile-vm", "public partial record ProfileModel", "Generated from ProfileModel (MVUX).")
P_NAME = member(VM_PROFILE, "prop.profile.display-name", "property", "DisplayName", "f.profile-vm", "IFeed<string> DisplayName", "IFeed<string>")
P_REGION = member(VM_PROFILE, "prop.profile.region", "property", "Region", "f.profile-vm", "IFeed<string> Region", "IFeed<string>")
C_SIGN_OUT = member(VM_PROFILE, "cmd.profile.sign-out", "command", "SignOut", "f.profile-vm", "ValueTask SignOut", "ICommand (generated)", **{"async": True})

VM_SETTINGS = viewmodel("vm.settings", "SettingsViewModel", "SettingsModel", "f.settings-vm", "public partial record SettingsModel", "Generated from SettingsModel (MVUX).")
P_WIFI = member(VM_SETTINGS, "prop.settings.upload-on-wifi-only", "property", "UploadOnWifiOnly", "f.settings-vm", "IState<bool> UploadOnWifiOnly", "IState<bool>", writable=True)
P_STYLES = member(VM_SETTINGS, "prop.settings.map-styles", "property", "MapStyles", "f.settings-vm", "IListFeed<string> MapStyles", "IListFeed<string>")
P_STYLE = member(VM_SETTINGS, "prop.settings.map-style", "property", "MapStyle", "f.settings-vm", "IState<string> MapStyle", "IState<string>", writable=True)

# dependencies (declared)
for a, b, fid, needle in [
    (P_PHOTO_COUNT, P_PHOTOS, "f.job-vm", "IFeed<int> PhotoCount"),
    (P_HAS_SIG, P_SIGNATURE, "f.job-vm", "IFeed<bool> HasSignature"),
    (P_CAN_COMPLETE, P_HAS_SIG, "f.job-vm", "IFeed<bool> CanComplete"),
    (P_CAN_COMPLETE, P_PHOTO_COUNT, "f.job-vm", "IFeed<bool> CanComplete"),
    (P_CAN_COMPLETE, P_STATUS, "f.job-vm", "IFeed<bool> CanComplete"),
    (P_CAN_COMPLETE, P_COMPLETING, "f.job-vm", "IFeed<bool> CanComplete"),
    (P_CAN_CAPTURE, P_NO_CAMERA, "f.photos-vm", "IFeed<bool> CanCapture"),
    (P_CAN_CAPTURE, P_CAPTURING, "f.photos-vm", "IFeed<bool> CanCapture"),
    (P_CAN_SYNC, P_SYNCING, "f.outbox-vm", "IFeed<bool> CanSync"),
    (P_FAILED, P_ITEMS, "f.outbox-vm", "IFeed<int> FailedCount"),
]:
    label = {P_CAN_COMPLETE + P_PHOTO_COUNT: ">= 1", P_CAN_COMPLETE + P_STATUS: "== OnSite", P_CAN_COMPLETE + P_COMPLETING: "negated",
             P_CAN_CAPTURE + P_NO_CAMERA: "negated", P_CAN_CAPTURE + P_CAPTURING: "negated", P_CAN_SYNC + P_SYNCING: "negated"}.get(a + b)
    edge(a, "depends-on", b, ref(fid, needle), label=label)

# inferred: two models read the same store, at different confidence
edge(P_PENDING, "depends-on", P_ITEMS, label="via IOutboxStore.Pending", evidence=ev(
    "inferred", 0.75, ref("f.today-vm", "PendingUploads =>"),
    "TodayModel.PendingUploads and OutboxModel.Items both read IOutboxStore.Pending. Neither model references the other; the link is inferred from the shared store."))
edge(P_UNSYNCED, "depends-on", P_FAILED, label="via IOutboxStore", evidence=ev(
    "inferred", 0.55, ref("f.job-vm", "HasUnsyncedChangesSinceLastSuccessfulUpload =>"),
    "HasPendingFor and FailedCount are both derived from IOutboxStore, but HasPendingFor's implementation is not in the excerpts; a failed upload probably keeps the job unsynced."))

# ------------------------------------------------------------------------------------------- components

COMP_CHIP = node("component.status-chip", "component", "StatusChip", ref("f.status-chip", "<UserControl x:Class"),
                 summary="Coloured pill for a job or upload status.",
                 properties={"uno": {"type": "UserControl", "class": "Fieldwork.Controls.StatusChip"}, "parts": ["background", "label"], "dependencyProperties": ["Status"]})
COMP_TILE = node("component.photo-tile", "component", "PhotoTile", ref("f.photo-tile", "<UserControl x:Class"),
                 summary="Square thumbnail with a pending-upload marker.",
                 properties={"uno": {"type": "UserControl", "class": "Fieldwork.Controls.PhotoTile"}, "parts": ["image", "pending icon"], "dependencyProperties": ["Source", "IsPending"]})
COMP_LEGACY = node("component.legacy-banner", "component", "LegacyBanner", ref("f.legacy-banner", "<UserControl x:Class"),
                   summary="Migration notice from 1.x. Declared, but no page uses it.",
                   properties={"uno": {"type": "UserControl", "class": "Fieldwork.Controls.LegacyBanner"}, "parts": ["info bar"]})

# ------------------------------------------------------------------------------------------- screens

def screen(nid: str, name: str, feature: str, vm: str, fid: str, route_needle: str, vm_needle: str, summary: str,
           cls: str, route: str, parts: list[dict], entry: bool = False) -> str:
    props = {"route": route, "uno": {"type": "Page", "class": cls}}
    if entry:
        props["isEntry"] = True
    rationale = f'Route "{route}" is the default route.' if entry else None
    node(nid, "screen", name, {"file": fid, "line": 1 if FILES[fid]["startLine"] == 1 else FILES[fid]["startLine"]},
         evidence=ev(source=ref("f.app", route_needle), rationale=rationale), summary=summary, properties=props, preview={"parts": parts})
    edge(nid, "belongs-to", feature, evidence=ev("derived", 0.9))
    edge(nid, "uses-viewmodel", vm, ref("f.app", vm_needle))
    return nid


def inst(nid: str, name: str, host: str, fid: str, needle: str, utype: str, xname: str | None, bounds: dict | None,
         end: str | None = None, summary: str | None = None, of: str | None = None, **uno) -> str:
    s = ref(fid, needle, end)
    u = {"type": utype, **uno}
    if xname:
        u["xName"] = xname
    node(nid, "component-instance", name, s, evidence=ev(source=s), summary=summary, properties={"uno": u},
         preview={"bounds": bounds} if bounds else None)
    edge(host, "contains", nid, s)
    if of:
        edge(nid, "instance-of", of, s)
    return nid


def bind(i: str, prop: str, label: str, fid: str, needle: str) -> None:
    edge(i, "binds-to", prop, ref(fid, needle), label=label)


def state(nid: str, name: str, owner: str, fid: str, needle: str, trigger: str, overrides: list[dict] | None = None,
          end: str | None = None, kind: str = "declared", confidence: float = 1.0, rationale: str | None = None,
          dim: list[str] | None = None, hide: list[str] | None = None, **uno) -> str:
    s = ref(fid, needle, end)
    preview: dict = {"overrides": overrides or []}
    if dim:
        preview["dim"] = dim
    if hide:
        preview["hide"] = hide
    node(nid, "state", name, s, evidence=ev(kind, confidence, s, rationale), properties={"trigger": trigger, "uno": uno}, preview=preview)
    edge(owner, "has-state", nid, s)
    return nid


def route(nid: str, name: str, origin: str, target: str, fid: str, needle: str, app_needle: str, request: str,
          origin_label: str, mechanism: str = "Navigation.Request", data: str | None = None, rationale: str | None = None) -> str:
    s = ref(fid, needle)
    props = {"request": request, "mechanism": mechanism}
    if data:
        props["data"] = data
    node(nid, "route", name, s, evidence=ev(source=s, rationale=rationale), properties=props)
    edge(origin, "entered-via", nid, s, label=origin_label)
    edge(nid, "navigates-to", target, ref("f.app", app_needle))
    return nid


# Today: entry, six states
S_TODAY = screen("screen.today", "Today", F_JOBS, VM_TODAY, "f.today-xaml", 'new ("Today"', "new ViewMap<TodayPage",
                 "Entry screen. Today's jobs, offline banner, uploads waiting.", "Fieldwork.Presentation.TodayPage", "Today", [
                     {"kind": "badge", "rect": R(0.07, 0.04, 0.12, 0.065), "label": "MK"},
                     {"kind": "heading", "rect": R(0.22, 0.045, 0.4, 0.06), "label": "Today"},
                     {"kind": "badge", "rect": R(0.66, 0.04, 0.27, 0.065), "label": "3 to upload"},
                     {"kind": "alert", "rect": R(0.07, 0.13, 0.86, 0.075), "label": "Offline. Last synced 08:42."},
                     {"kind": "list", "rect": R(0.07, 0.235, 0.86, 0.72), "rows": 5, "items": [
                         "Hartley Dental · On site", "Rue Saint-Denis 4412 · Travelling", "Mapleview Clinic · Scheduled",
                         "Bergeron & Fils, entrepôt nord · Scheduled", "Westmount Library · Done"]},
                 ], entry=True)
I_AVATAR = inst("inst.today.avatar", "Avatar button", S_TODAY, "f.today-xaml", 'x:Name="AvatarButton"', "Button", "AvatarButton", R(0.07, 0.04, 0.12, 0.065), end='uen:Navigation.Request="Profile"')
I_OUTBOX_BTN = inst("inst.today.outbox-button", "Outbox button", S_TODAY, "f.today-xaml", 'x:Name="OutboxButton"', "Button", "OutboxButton", R(0.66, 0.04, 0.27, 0.065),
                    end="Content=\"{Binding PendingUploads", summary="Shows how many items are waiting to upload.")
I_OFFLINE = inst("inst.today.offline-banner", "Offline banner", S_TODAY, "f.today-xaml", 'x:Name="OfflineBanner"', "InfoBar", "OfflineBanner", R(0.07, 0.13, 0.86, 0.075),
                 end='Message="{Binding LastSyncedLabel}"', summary="Collapsed while online.")
I_JOBS_FEED = inst("inst.today.jobs-feed", "Jobs feed", S_TODAY, "f.today-xaml", 'x:Name="JobsFeed"', "FeedView", "JobsFeed", R(0.07, 0.235, 0.86, 0.72),
                   end="</mvux:FeedView>")
I_JOB_LIST = inst("inst.today.job-list", "Job list", I_JOBS_FEED, "f.today-xaml", 'x:Name="JobList"', "ListView", "JobList", R(0.08, 0.245, 0.84, 0.7), end="</ListView>")
I_TODAY_CHIP = inst("inst.today.status-chip", "StatusChip (Today)", I_JOB_LIST, "f.today-xaml", "<controls:StatusChip Grid.Column=\"1\" Status=\"{Binding Status}\"", "StatusChip", None, R(0.68, 0.258, 0.22, 0.05), of=COMP_CHIP)
bind(I_OUTBOX_BTN, P_PENDING, "Content", "f.today-xaml", "Content=\"{Binding PendingUploads")
bind(I_OFFLINE, P_OFFLINE, "IsOpen", "f.today-xaml", 'IsOpen="{Binding IsOffline}"')
bind(I_OFFLINE, P_SYNCED, "Message", "f.today-xaml", 'Message="{Binding LastSyncedLabel}"')
bind(I_JOBS_FEED, P_JOBS, "Source", "f.today-xaml", 'Source="{Binding Jobs}"')
edge(I_JOBS_FEED, "invokes", C_REFRESH, ref("f.today-xaml", 'RefreshCommand="{Binding Refresh}"'), label="RefreshCommand")

ST_T_LOADING = state("state.today.loading", "Loading", S_TODAY, "f.today-xaml", "<mvux:FeedView.ProgressTemplate>", "Jobs feed is loading",
                     [{"kind": "spinner", "rect": R(0.07, 0.235, 0.86, 0.72)}], end="</mvux:FeedView.ProgressTemplate>",
                     rationale="FeedView.ProgressTemplate", mechanism="FeedView.ProgressTemplate")
ST_T_EMPTY = state("state.today.empty", "Empty", S_TODAY, "f.today-xaml", "<mvux:FeedView.NoneTemplate>", "Jobs feed has no items",
                   [{"kind": "empty", "rect": R(0.07, 0.235, 0.86, 0.72), "label": "No jobs today. Enjoy the quiet."}], end="</mvux:FeedView.NoneTemplate>",
                   rationale="FeedView.NoneTemplate", mechanism="FeedView.NoneTemplate")
ST_T_ERROR = state("state.today.error", "Error", S_TODAY, "f.today-xaml", "<mvux:FeedView.ErrorTemplate>", "Jobs feed failed",
                   [{"kind": "alert", "rect": R(0.07, 0.235, 0.86, 0.09), "label": "Couldn't load today's jobs"}], end="</mvux:FeedView.ErrorTemplate>",
                   rationale="FeedView.ErrorTemplate", mechanism="FeedView.ErrorTemplate")
ST_T_POPULATED = state("state.today.populated", "Populated", S_TODAY, "f.today-xaml", "<ListView x:Name=\"JobList\"", "Jobs feed has items",
                       kind="derived", confidence=0.9, rationale="The FeedView's data template.", mechanism="FeedView data template")
ST_T_OFFLINE = state("state.today.offline", "Offline", S_TODAY, "f.today-xaml", 'IsOpen="{Binding IsOffline}"', "IsOffline == true",
                     [{"kind": "alert", "rect": R(0.07, 0.13, 0.86, 0.075), "label": "Offline. Last synced 08:42."}],
                     kind="observed", confidence=0.8, rationale="Seen in a recorded session with the network disabled; the banner opened within a second.",
                     member="IsOffline")
ST_T_REFRESH = state("state.today.refreshing", "Refreshing", S_TODAY, "f.today-xaml", 'RefreshCommand="{Binding Refresh}"', "Refresh is running",
                     [{"kind": "bar", "rect": R(0.07, 0.215, 0.86, 0.012)}], kind="derived", confidence=0.85,
                     rationale="FeedView shows its refresh indicator while RefreshCommand runs.", member="Refresh")
edge(C_REFRESH, "transitions-to", ST_T_REFRESH, ref("f.today-vm", "ValueTask Refresh"), label="pull to refresh")
edge(ST_T_REFRESH, "transitions-to", ST_T_POPULATED, ref("f.today-vm", "ValueTask Refresh"), label="refreshed")
edge(ST_T_LOADING, "transitions-to", ST_T_POPULATED, evidence=ev("derived", 0.9), label="items arrive")
edge(ST_T_LOADING, "transitions-to", ST_T_EMPTY, evidence=ev("derived", 0.9), label="no items")
edge(ST_T_LOADING, "transitions-to", ST_T_ERROR, evidence=ev("derived", 0.9), label="exception")
edge(ST_T_OFFLINE, "depends-on", P_OFFLINE, ref("f.today-xaml", 'IsOpen="{Binding IsOffline}"'))

# Job: shared view model, many members, five states
S_JOB = screen("screen.job", "Job", F_JOBS, VM_JOB, "f.job-xaml", 'new ("Job"', "new ViewMap<JobPage",
               "One job: customer, site, notes, photos, signature, complete.", "Fieldwork.Presentation.JobPage", "Job", [
                   {"kind": "heading", "rect": R(0.07, 0.045, 0.6, 0.06), "label": "Hartley Dental"},
                   {"kind": "badge", "rect": R(0.7, 0.045, 0.23, 0.055), "label": "On site"},
                   {"kind": "text", "rect": R(0.07, 0.125, 0.6, 0.05), "label": "1180 Greene Ave, Westmount"},
                   {"kind": "badge", "rect": R(0.07, 0.178, 0.3, 0.028), "label": "Not synced"},
                   {"kind": "button", "rect": R(0.74, 0.12, 0.19, 0.06), "label": "Map", "emphasis": "secondary"},
                   {"kind": "input", "rect": R(0.07, 0.21, 0.86, 0.16), "label": "Notes"},
                   {"kind": "image", "rect": R(0.07, 0.4, 0.86, 0.14)},
                   {"kind": "button", "rect": R(0.07, 0.57, 0.4, 0.065), "label": "2 photos", "emphasis": "secondary"},
                   {"kind": "button", "rect": R(0.53, 0.57, 0.4, 0.065), "label": "Customer signature", "emphasis": "secondary"},
                   {"kind": "button", "rect": R(0.07, 0.86, 0.86, 0.08), "label": "Complete job", "emphasis": "primary"},
               ])
I_JOB_FEED = inst("inst.job.job-feed", "Job feed", S_JOB, "f.job-xaml", 'x:Name="JobFeed"', "FeedView", "JobFeed", R(0.02, 0.02, 0.96, 0.96), end="</mvux:FeedView>")
I_CUSTOMER = inst("inst.job.customer", "Customer name", S_JOB, "f.job-xaml", 'x:Name="CustomerText"', "TextBlock", "CustomerText", R(0.07, 0.045, 0.6, 0.06))
I_STATUS_HDR = inst("inst.job.status-chip", "StatusChip (Job)", S_JOB, "f.job-xaml", 'x:Name="StatusHeader"', "StatusChip", "StatusHeader", R(0.7, 0.045, 0.23, 0.055),
                    end='Status="{Binding Status}" />', of=COMP_CHIP)
I_SYNC_CHIP = inst("inst.job.sync-chip", "StatusChip (sync)", S_JOB, "f.job-xaml", 'x:Name="SyncChip"', "StatusChip", "SyncChip", R(0.07, 0.178, 0.3, 0.028),
                   end="Converter={StaticResource SyncStatus}}", of=COMP_CHIP, summary="A second StatusChip on the same page, for upload state.")
I_ADDRESS = inst("inst.job.address", "Site address", S_JOB, "f.job-xaml", 'x:Name="AddressText"', "TextBlock", "AddressText", R(0.07, 0.125, 0.6, 0.05))
I_MAP_BTN = inst("inst.job.map-button", "Map button", S_JOB, "f.job-xaml", 'x:Name="MapButton"', "Button", "MapButton", R(0.74, 0.12, 0.19, 0.06), end='uen:Navigation.Request="Map"')
I_NOTES = inst("inst.job.notes", "Notes box", S_JOB, "f.job-xaml", 'x:Name="NotesBox"', "TextBox", "NotesBox", R(0.07, 0.21, 0.86, 0.16), end='Text="{Binding Notes, Mode=TwoWay}"')
I_STRIP = inst("inst.job.photo-strip", "Photo strip", S_JOB, "f.job-xaml", 'x:Name="PhotoStrip"', "ItemsRepeater", "PhotoStrip", R(0.07, 0.4, 0.86, 0.14), end="</ItemsRepeater>")
I_JOB_TILE = inst("inst.job.photo-tile", "PhotoTile (Job)", I_STRIP, "f.job-xaml", "<DataTemplate><controls:PhotoTile", "PhotoTile", None, R(0.08, 0.41, 0.2, 0.12), of=COMP_TILE)
I_PHOTOS_BTN = inst("inst.job.photos-button", "Photos button", S_JOB, "f.job-xaml", 'x:Name="PhotosButton"', "Button", "PhotosButton", R(0.07, 0.57, 0.4, 0.065), end='uen:Navigation.Request="Photos"')
I_SIGN_BTN = inst("inst.job.sign-button", "Signature button", S_JOB, "f.job-xaml", 'x:Name="SignButton"', "Button", "SignButton", R(0.53, 0.57, 0.4, 0.065), end='uen:Navigation.Request="Signature"')
I_COMPLETE = inst("inst.job.complete", "Complete job", S_JOB, "f.job-xaml", 'x:Name="CompleteButton"', "Button", "CompleteButton", R(0.07, 0.86, 0.86, 0.08),
                  end="</Button>", summary="Primary action. Disabled until CanComplete is true.", styleKey="FilledButtonStyle")
bind(I_JOB_FEED, P_JOB, "Source", "f.job-xaml", 'Source="{Binding Job}"')
bind(I_STATUS_HDR, P_STATUS, "Status", "f.job-xaml", 'Status="{Binding Status}" />')
bind(I_SYNC_CHIP, P_UNSYNCED, "Status", "f.job-xaml", "Status=\"{Binding HasUnsyncedChangesSinceLastSuccessfulUpload")
bind(I_NOTES, P_NOTES, "Text (TwoWay)", "f.job-xaml", 'Text="{Binding Notes, Mode=TwoWay}"')
bind(I_STRIP, P_PHOTOS, "ItemsSource", "f.job-xaml", 'ItemsSource="{Binding Photos}"')
bind(I_PHOTOS_BTN, P_PHOTO_COUNT, "Content", "f.job-xaml", "Content=\"{Binding PhotoCount")
bind(I_COMPLETE, P_CAN_COMPLETE, "IsEnabled", "f.job-xaml", 'IsEnabled="{Binding CanComplete}"')
bind(I_COMPLETE, P_COMPLETING, "ProgressRing.IsActive", "f.job-xaml", '<ProgressRing IsActive="{Binding IsCompleting}" />')
edge(I_COMPLETE, "invokes", C_COMPLETE, ref("f.job-xaml", 'Command="{Binding Complete}"'), label="Command")

ST_J_LOADING = state("state.job.loading", "Loading", S_JOB, "f.job-xaml", "<mvux:FeedView.ProgressTemplate>", "Job feed is loading",
                     [{"kind": "spinner", "rect": R(0.07, 0.12, 0.86, 0.82)}], end="</mvux:FeedView.ProgressTemplate>",
                     rationale="FeedView.ProgressTemplate", mechanism="FeedView.ProgressTemplate")
ST_J_READY = state("state.job.ready", "Ready", S_JOB, "f.job-vm", "IFeed<bool> CanComplete", "CanComplete == true", kind="derived", confidence=0.9,
                   rationale="Signature present, one photo or more, status OnSite.", member="CanComplete")
ST_J_COMPLETING = state("state.job.completing", "Completing", S_JOB, "f.job-xaml", '<ProgressRing IsActive="{Binding IsCompleting}" />', "IsCompleting == true",
                        [{"kind": "button", "rect": R(0.07, 0.86, 0.86, 0.08), "label": "Complete job  ◌", "emphasis": "disabled"}],
                        rationale="ProgressRing.IsActive bound to IsCompleting; CanComplete is false while completing.", dim=[I_NOTES], member="IsCompleting")
ST_J_ERROR = state("state.job.error", "Error", S_JOB, "f.job-xaml", "<mvux:FeedView.ErrorTemplate>", "LastError != null",
                   [{"kind": "alert", "rect": R(0.07, 0.75, 0.86, 0.08), "label": "Upload failed. Saved to outbox."}], end="</mvux:FeedView.ErrorTemplate>",
                   rationale="FeedView.ErrorTemplate shows LastError.", member="LastError")
ST_J_DISABLED = state("state.complete.disabled", "Disabled", I_COMPLETE, "f.job-xaml", 'IsEnabled="{Binding CanComplete}"', "CanComplete == false",
                      [{"kind": "button", "rect": R(0.07, 0.86, 0.86, 0.08), "label": "Complete job", "emphasis": "disabled"}],
                      rationale="IsEnabled bound to CanComplete.", member="CanComplete")
edge(C_COMPLETE, "transitions-to", ST_J_COMPLETING, ref("f.job-vm", "await IsCompleting.SetAsync(true, ct);"), label="IsCompleting = true")
edge(ST_J_COMPLETING, "transitions-to", ST_J_ERROR, ref("f.job-vm", "catch (JobException ex)"), label="JobException")
edge(ST_J_COMPLETING, "transitions-to", ST_J_READY, ref("f.job-vm", "await IsCompleting.SetAsync(false, ct);"), label="finally")
edge(ST_J_COMPLETING, "depends-on", P_COMPLETING, ref("f.job-xaml", '<ProgressRing IsActive="{Binding IsCompleting}" />'))
edge(ST_J_ERROR, "depends-on", P_JOB_ERROR, ref("f.job-vm", "await LastError.SetAsync(ex.Message, ct);"))
edge(ST_J_READY, "depends-on", P_CAN_COMPLETE, ref("f.job-vm", "IFeed<bool> CanComplete"))
edge(ST_J_DISABLED, "depends-on", P_CAN_COMPLETE, ref("f.job-xaml", 'IsEnabled="{Binding CanComplete}"'), label="== false")

# Map: one state pair, cycle back to Job
S_MAP = screen("screen.map", "Map", F_JOBS, VM_MAP, "f.map-xaml", 'new ("Map"', "new ViewMap<MapPage",
               "Jobs near the technician; a pin opens the job.", "Fieldwork.Presentation.MapPage", "Map", [
                   {"kind": "heading", "rect": R(0.07, 0.045, 0.6, 0.06), "label": "Nearby jobs"},
                   {"kind": "image", "rect": R(0.0, 0.13, 1.0, 0.87)},
               ])
I_PINS_FEED = inst("inst.map.pins-feed", "Pins feed", S_MAP, "f.map-xaml", 'x:Name="PinsFeed"', "FeedView", "PinsFeed", R(0.0, 0.13, 1.0, 0.87), end="</mvux:FeedView>")
I_MAP = inst("inst.map.job-map", "Job map", I_PINS_FEED, "f.map-xaml", 'x:Name="JobMap"', "MapView", "JobMap", R(0.0, 0.13, 1.0, 0.87), end='uen:Navigation.Request="Job" />')
bind(I_PINS_FEED, P_PINS, "Source", "f.map-xaml", 'Source="{Binding JobPins}"')
bind(I_MAP, P_SELECTED, "SelectedPin (TwoWay)", "f.map-xaml", 'SelectedPin="{Binding SelectedJob, Mode=TwoWay}"')
state("state.map.loading", "Loading", S_MAP, "f.map-xaml", "<mvux:FeedView.ProgressTemplate>", "Pins feed is loading",
      [{"kind": "spinner", "rect": R(0.0, 0.13, 1.0, 0.87)}], end="</mvux:FeedView.ProgressTemplate>", rationale="FeedView.ProgressTemplate", mechanism="FeedView.ProgressTemplate")
state("state.map.populated", "Populated", S_MAP, "f.map-xaml", 'x:Name="JobMap"', "Pins feed has items", kind="derived", confidence=0.9,
      rationale="The FeedView's data template.", mechanism="FeedView data template")

# Photos
S_PHOTOS = screen("screen.photos", "Photos", F_CAPTURE, VM_PHOTOS, "f.photos-xaml", 'new ("Photos"', "new ViewMap<PhotosPage",
                  "Capture and review photos for the job.", "Fieldwork.Presentation.PhotosPage", "Photos", [
                      {"kind": "heading", "rect": R(0.07, 0.045, 0.5, 0.06), "label": "Photos"},
                      {"kind": "list", "rect": R(0.07, 0.13, 0.86, 0.68), "rows": 3, "items": ["Panel before · 09:12", "Panel after · 09:47", "Serial plate · 09:49"]},
                      {"kind": "button", "rect": R(0.07, 0.86, 0.86, 0.08), "label": "Take photo", "emphasis": "primary"},
                  ])
I_CAM_WARN = inst("inst.photos.camera-warning", "Camera warning", S_PHOTOS, "f.photos-xaml", 'x:Name="CameraWarning"', "InfoBar", "CameraWarning", R(0.07, 0.13, 0.86, 0.075),
                  end='Message="No camera found.', summary="Collapsed while a camera is available.")
I_PHOTOS_FEED = inst("inst.photos.photos-feed", "Photos feed", S_PHOTOS, "f.photos-xaml", 'x:Name="PhotosFeed"', "FeedView", "PhotosFeed", R(0.07, 0.13, 0.86, 0.68), end="</mvux:FeedView>")
I_GRID = inst("inst.photos.grid", "Photo grid", I_PHOTOS_FEED, "f.photos-xaml", 'x:Name="PhotoGrid"', "GridView", "PhotoGrid", R(0.08, 0.14, 0.84, 0.66), end="</GridView>")
I_GRID_TILE = inst("inst.photos.photo-tile", "PhotoTile (Photos)", I_GRID, "f.photos-xaml", "<DataTemplate><controls:PhotoTile", "PhotoTile", None, R(0.09, 0.15, 0.26, 0.16), of=COMP_TILE)
I_CAPTURE = inst("inst.photos.capture", "Take photo", S_PHOTOS, "f.photos-xaml", 'x:Name="CaptureButton"', "Button", "CaptureButton", R(0.07, 0.86, 0.86, 0.08), end="</Button>", styleKey="FilledButtonStyle")
bind(I_CAM_WARN, P_NO_CAMERA, "IsOpen", "f.photos-xaml", 'IsOpen="{Binding CameraUnavailable}"')
bind(I_PHOTOS_FEED, P_PHOTO_LIST, "Source", "f.photos-xaml", 'Source="{Binding Photos}"')
bind(I_CAPTURE, P_CAN_CAPTURE, "IsEnabled", "f.photos-xaml", 'IsEnabled="{Binding CanCapture}"')
bind(I_CAPTURE, P_CAPTURING, "ProgressRing.IsActive", "f.photos-xaml", '<ProgressRing IsActive="{Binding IsCapturing}" />')
edge(I_CAPTURE, "invokes", C_CAPTURE, ref("f.photos-xaml", 'Command="{Binding Capture}"'), label="Command")
ST_P_EMPTY = state("state.photos.empty", "Empty", S_PHOTOS, "f.photos-xaml", "<mvux:FeedView.NoneTemplate>", "No photos yet",
                   [{"kind": "empty", "rect": R(0.07, 0.13, 0.86, 0.68), "label": "No photos yet. Capture at least one before completing."}],
                   end="</mvux:FeedView.NoneTemplate>", rationale="FeedView.NoneTemplate", mechanism="FeedView.NoneTemplate")
ST_P_POP = state("state.photos.populated", "Populated", S_PHOTOS, "f.photos-xaml", 'x:Name="PhotoGrid"', "Photos has items", kind="derived", confidence=0.9,
                 rationale="The FeedView's data template.", mechanism="FeedView data template")
ST_P_CAPTURING = state("state.photos.capturing", "Capturing", S_PHOTOS, "f.photos-xaml", '<ProgressRing IsActive="{Binding IsCapturing}" />', "IsCapturing == true",
                       [{"kind": "button", "rect": R(0.07, 0.86, 0.86, 0.08), "label": "Take photo  ◌", "emphasis": "disabled"}], member="IsCapturing")
ST_P_NOCAM = state("state.photos.camera-unavailable", "Camera unavailable", S_PHOTOS, "f.photos-xaml", 'IsOpen="{Binding CameraUnavailable}"', "CameraUnavailable == true",
                   [{"kind": "alert", "rect": R(0.07, 0.13, 0.86, 0.075), "label": "No camera found."},
                    {"kind": "button", "rect": R(0.07, 0.86, 0.86, 0.08), "label": "Take photo", "emphasis": "disabled"}], member="CameraUnavailable")
edge(C_CAPTURE, "transitions-to", ST_P_CAPTURING, ref("f.photos-vm", "await IsCapturing.SetAsync(true, ct);"), label="IsCapturing = true")
edge(ST_P_CAPTURING, "transitions-to", ST_P_POP, ref("f.photos-vm", "finally { await IsCapturing.SetAsync(false, ct); }"), label="finally")
edge(ST_P_EMPTY, "transitions-to", ST_P_CAPTURING, evidence=ev("derived", 0.8), label="take photo")
edge(ST_P_CAPTURING, "depends-on", P_CAPTURING, ref("f.photos-xaml", '<ProgressRing IsActive="{Binding IsCapturing}" />'))
edge(ST_P_NOCAM, "depends-on", P_NO_CAMERA, ref("f.photos-xaml", 'IsOpen="{Binding CameraUnavailable}"'))

# Signature: different feature, same view model as Job
S_SIG = screen("screen.signature", "Signature", F_CAPTURE, VM_JOB, "f.signature-xaml", 'new ("Signature"', "new ViewMap<SignaturePage",
               "Customer signs on the device.", "Fieldwork.Presentation.SignaturePage", "Signature", [
                   {"kind": "heading", "rect": R(0.07, 0.045, 0.7, 0.06), "label": "Customer signature"},
                   {"kind": "image", "rect": R(0.07, 0.13, 0.86, 0.68)},
                   {"kind": "button", "rect": R(0.4, 0.86, 0.24, 0.07), "label": "Clear", "emphasis": "secondary"},
                   {"kind": "button", "rect": R(0.67, 0.86, 0.26, 0.07), "label": "Done", "emphasis": "primary"},
               ])
I_PAD = inst("inst.signature.pad", "Signature pad", S_SIG, "f.signature-xaml", 'x:Name="Pad"', "SignaturePad", "Pad", R(0.07, 0.13, 0.86, 0.68), end='Strokes="{Binding Signature, Mode=TwoWay}"')
I_CLEAR = inst("inst.signature.clear", "Clear", S_SIG, "f.signature-xaml", 'x:Name="ClearButton"', "Button", "ClearButton", R(0.4, 0.86, 0.24, 0.07))
I_DONE = inst("inst.signature.done", "Done", S_SIG, "f.signature-xaml", 'x:Name="DoneButton"', "Button", "DoneButton", R(0.67, 0.86, 0.26, 0.07), end='uen:Navigation.Request="-"', styleKey="FilledButtonStyle")
bind(I_PAD, P_SIGNATURE, "Strokes (TwoWay)", "f.signature-xaml", 'Strokes="{Binding Signature, Mode=TwoWay}"')
bind(I_DONE, P_HAS_SIG, "IsEnabled", "f.signature-xaml", 'IsEnabled="{Binding HasSignature}"')
edge(I_CLEAR, "invokes", C_CLEAR_SIG, ref("f.signature-xaml", 'x:Name="ClearButton"'), label="Command")
ST_S_BLANK = state("state.signature.blank", "Blank", S_SIG, "f.signature-xaml", 'IsEnabled="{Binding HasSignature}"', "HasSignature == false",
                   [{"kind": "button", "rect": R(0.67, 0.86, 0.26, 0.07), "label": "Done", "emphasis": "disabled"}],
                   kind="derived", confidence=0.9, rationale="Done is disabled until there is a signature.", member="HasSignature")
ST_S_SIGNED = state("state.signature.signed", "Signed", S_SIG, "f.signature-xaml", 'Strokes="{Binding Signature, Mode=TwoWay}"', "HasSignature == true",
                    kind="derived", confidence=0.9, rationale="The pad writes strokes into Signature; HasSignature follows.", member="HasSignature")
edge(ST_S_BLANK, "transitions-to", ST_S_SIGNED, evidence=ev("derived", 0.9), label="first stroke")
edge(C_CLEAR_SIG, "transitions-to", ST_S_BLANK, ref("f.job-vm", "ValueTask ClearSignature"), label="Signature = null")
edge(ST_S_BLANK, "depends-on", P_HAS_SIG, ref("f.signature-xaml", 'IsEnabled="{Binding HasSignature}"'), label="== false")
edge(ST_S_SIGNED, "depends-on", P_HAS_SIG, ref("f.signature-xaml", 'IsEnabled="{Binding HasSignature}"'))

# Outbox
S_OUTBOX = screen("screen.outbox", "Outbox", F_SYNC, VM_OUTBOX, "f.outbox-xaml", 'new ("Outbox"', "new ViewMap<OutboxPage",
                  "Uploads waiting for a connection, and what failed.", "Fieldwork.Presentation.OutboxPage", "Outbox", [
                      {"kind": "heading", "rect": R(0.07, 0.045, 0.5, 0.06), "label": "Outbox"},
                      {"kind": "text", "rect": R(0.07, 0.125, 0.86, 0.05), "label": "1 failed"},
                      {"kind": "list", "rect": R(0.07, 0.2, 0.86, 0.6), "rows": 3, "items": ["Hartley Dental, 2 photos · Failed", "Mapleview Clinic, notes · Pending", "Westmount Library, signature · Pending"]},
                      {"kind": "button", "rect": R(0.07, 0.86, 0.86, 0.08), "label": "Sync now", "emphasis": "primary"},
                  ])
I_FAILED = inst("inst.outbox.failed", "Failed count", S_OUTBOX, "f.outbox-xaml", 'x:Name="FailedText"', "TextBlock", "FailedText", R(0.07, 0.125, 0.86, 0.05), end="Text=\"{Binding FailedCount")
I_OUTBOX_FEED = inst("inst.outbox.feed", "Outbox feed", S_OUTBOX, "f.outbox-xaml", 'x:Name="OutboxFeed"', "FeedView", "OutboxFeed", R(0.07, 0.2, 0.86, 0.6), end="</mvux:FeedView>")
I_OUTBOX_LIST = inst("inst.outbox.list", "Outbox list", I_OUTBOX_FEED, "f.outbox-xaml", 'x:Name="OutboxList"', "ListView", "OutboxList", R(0.08, 0.21, 0.84, 0.58), end="</ListView>")
I_OUTBOX_CHIP = inst("inst.outbox.status-chip", "StatusChip (Outbox)", I_OUTBOX_LIST, "f.outbox-xaml", 'Status="{Binding State}"', "StatusChip", None, R(0.68, 0.222, 0.22, 0.05), of=COMP_CHIP)
I_SYNC = inst("inst.outbox.sync-now", "Sync now", S_OUTBOX, "f.outbox-xaml", 'x:Name="SyncNowButton"', "Button", "SyncNowButton", R(0.07, 0.86, 0.86, 0.08), end="</Button>", styleKey="FilledButtonStyle")
bind(I_FAILED, P_FAILED, "Text", "f.outbox-xaml", "Text=\"{Binding FailedCount")
bind(I_OUTBOX_FEED, P_ITEMS, "Source", "f.outbox-xaml", 'Source="{Binding Items}"')
bind(I_SYNC, P_CAN_SYNC, "IsEnabled", "f.outbox-xaml", 'IsEnabled="{Binding CanSync}"')
bind(I_SYNC, P_SYNCING, "ProgressRing.IsActive", "f.outbox-xaml", '<ProgressRing IsActive="{Binding IsSyncing}" />')
edge(I_SYNC, "invokes", C_SYNC, ref("f.outbox-xaml", 'Command="{Binding SyncNow}"'), label="Command")
ST_O_EMPTY = state("state.outbox.empty", "All synced", S_OUTBOX, "f.outbox-xaml", "<mvux:FeedView.NoneTemplate>", "Nothing pending",
                   [{"kind": "empty", "rect": R(0.07, 0.2, 0.86, 0.6), "label": "Everything is synced."}], end="</mvux:FeedView.NoneTemplate>",
                   rationale="FeedView.NoneTemplate", mechanism="FeedView.NoneTemplate")
ST_O_PENDING = state("state.outbox.pending", "Pending", S_OUTBOX, "f.outbox-xaml", 'x:Name="OutboxList"', "Items has entries", kind="derived", confidence=0.9,
                     rationale="The FeedView's data template.", mechanism="FeedView data template")
ST_O_SYNCING = state("state.outbox.syncing", "Syncing", S_OUTBOX, "f.outbox-xaml", '<ProgressRing IsActive="{Binding IsSyncing}" />', "IsSyncing == true",
                     [{"kind": "button", "rect": R(0.07, 0.86, 0.86, 0.08), "label": "Sync now  ◌", "emphasis": "disabled"}], member="IsSyncing")
ST_O_FAILED = state("state.outbox.failed", "Failed", S_OUTBOX, "f.outbox-xaml", "Text=\"{Binding FailedCount", "FailedCount > 0",
                    [{"kind": "alert", "rect": R(0.07, 0.125, 0.86, 0.05), "label": "1 failed"}], kind="derived", confidence=0.85,
                    rationale="The failed label is the only signal; the list keeps failed items in place.", member="FailedCount")
edge(C_SYNC, "transitions-to", ST_O_SYNCING, ref("f.outbox-vm", "await IsSyncing.SetAsync(true, ct);"), label="IsSyncing = true")
edge(ST_O_SYNCING, "transitions-to", ST_O_EMPTY, ref("f.outbox-vm", "try { await Sync.PushAsync(ct); }"), label="pushed")
edge(ST_O_SYNCING, "transitions-to", ST_O_FAILED, evidence=ev("derived", 0.7, rationale="PushAsync leaves failed items in the store; FailedCount rises."), label="some failed")
edge(ST_O_SYNCING, "depends-on", P_SYNCING, ref("f.outbox-xaml", '<ProgressRing IsActive="{Binding IsSyncing}" />'))
edge(ST_O_FAILED, "depends-on", P_FAILED, ref("f.outbox-xaml", "Text=\"{Binding FailedCount"))

# Profile and Settings: no states
S_PROFILE = screen("screen.profile", "Profile", F_ACCOUNT, VM_PROFILE, "f.profile-xaml", 'new ("Profile"', "new ViewMap<ProfilePage",
                   "The signed-in technician.", "Fieldwork.Presentation.ProfilePage", "Profile", [
                       {"kind": "heading", "rect": R(0.07, 0.045, 0.7, 0.06), "label": "Marie-Kim Tremblay"},
                       {"kind": "text", "rect": R(0.07, 0.125, 0.7, 0.05), "label": "Montréal Ouest"},
                       {"kind": "button", "rect": R(0.07, 0.21, 0.86, 0.07), "label": "Settings", "emphasis": "secondary"},
                       {"kind": "button", "rect": R(0.07, 0.31, 0.86, 0.07), "label": "Sign out", "emphasis": "secondary"},
                   ])
I_NAME = inst("inst.profile.name", "Display name", S_PROFILE, "f.profile-xaml", 'x:Name="NameText"', "TextBlock", "NameText", R(0.07, 0.045, 0.7, 0.06))
I_REGION = inst("inst.profile.region", "Region", S_PROFILE, "f.profile-xaml", 'x:Name="RegionText"', "TextBlock", "RegionText", R(0.07, 0.125, 0.7, 0.05))
I_SETTINGS_BTN = inst("inst.profile.settings", "Settings button", S_PROFILE, "f.profile-xaml", 'x:Name="SettingsButton"', "Button", "SettingsButton", R(0.07, 0.21, 0.86, 0.07))
I_SIGNOUT = inst("inst.profile.sign-out", "Sign out", S_PROFILE, "f.profile-xaml", 'x:Name="SignOutButton"', "Button", "SignOutButton", R(0.07, 0.31, 0.86, 0.07))
bind(I_NAME, P_NAME, "Text", "f.profile-xaml", 'x:Name="NameText"')
bind(I_REGION, P_REGION, "Text", "f.profile-xaml", 'x:Name="RegionText"')
edge(I_SIGNOUT, "invokes", C_SIGN_OUT, ref("f.profile-xaml", 'x:Name="SignOutButton"'), label="Command")

S_SETTINGS = screen("screen.settings", "Settings", F_ACCOUNT, VM_SETTINGS, "f.settings-xaml", 'new ("Settings"', "new ViewMap<SettingsPage",
                    "Device preferences.", "Fieldwork.Presentation.SettingsPage", "Settings", [
                        {"kind": "heading", "rect": R(0.07, 0.045, 0.5, 0.06), "label": "Settings"},
                        {"kind": "stepper", "rect": R(0.07, 0.13, 0.86, 0.08), "label": "Upload photos on Wi-Fi only"},
                        {"kind": "input", "rect": R(0.07, 0.24, 0.86, 0.08), "label": "Map style · Streets"},
                    ])
I_WIFI = inst("inst.settings.wifi-only", "Wi-Fi only", S_SETTINGS, "f.settings-xaml", 'x:Name="WifiOnlyToggle"', "ToggleSwitch", "WifiOnlyToggle", R(0.07, 0.13, 0.86, 0.08), end='IsOn="{Binding UploadOnWifiOnly, Mode=TwoWay}"')
I_STYLE = inst("inst.settings.map-style", "Map style", S_SETTINGS, "f.settings-xaml", 'x:Name="MapStyleBox"', "ComboBox", "MapStyleBox", R(0.07, 0.24, 0.86, 0.08), end='ItemsSource="{Binding MapStyles}"')
bind(I_WIFI, P_WIFI, "IsOn (TwoWay)", "f.settings-xaml", 'IsOn="{Binding UploadOnWifiOnly, Mode=TwoWay}"')
bind(I_STYLE, P_STYLES, "ItemsSource", "f.settings-xaml", 'ItemsSource="{Binding MapStyles}"')
bind(I_STYLE, P_STYLE, "SelectedItem (TwoWay)", "f.settings-xaml", 'ItemsSource="{Binding MapStyles}"')

# ------------------------------------------------------------------------------------------- routes

route("route.today-to-job", "Today → Job", I_JOB_LIST, S_JOB, "f.today-xaml", 'uen:Navigation.Request="Job"', 'new ("Job"', "Job", "item click", data="Job",
      rationale='uen:Navigation.Request="Job" on the list; the clicked job is passed as data.')
route("route.today-to-outbox", "Today → Outbox", I_OUTBOX_BTN, S_OUTBOX, "f.today-xaml", 'uen:Navigation.Request="Outbox"', 'new ("Outbox"', "Outbox", "click")
route("route.today-to-profile", "Today → Profile", I_AVATAR, S_PROFILE, "f.today-xaml", 'uen:Navigation.Request="Profile"', 'new ("Profile"', "Profile", "click")
route("route.job-to-map", "Job → Map", I_MAP_BTN, S_MAP, "f.job-xaml", 'uen:Navigation.Request="Map"', 'new ("Map"', "Map", "click")
route("route.map-to-job", "Map → Job", I_MAP, S_JOB, "f.map-xaml", 'uen:Navigation.Request="Job" />', 'new ("Job"', "Job", "pin tap", data="JobPin",
      rationale="A pin opens its job; with Job → Map this is a cycle.")
route("route.job-to-photos", "Job → Photos", I_PHOTOS_BTN, S_PHOTOS, "f.job-xaml", 'uen:Navigation.Request="Photos"', 'new ("Photos"', "Photos", "click", data="Job")
route("route.job-to-signature", "Job → Signature", I_SIGN_BTN, S_SIG, "f.job-xaml", 'uen:Navigation.Request="Signature"', 'new ("Signature"', "Signature", "click")
route("route.signature-to-job", "Signature → Job", I_DONE, S_JOB, "f.signature-xaml", 'uen:Navigation.Request="-"', 'new ("Job"', "-", "click",
      mechanism="Navigation.Request (back)", rationale='"-" navigates back to the page that opened Signature, which is always Job.')
route("route.job-to-today", "Job → Today", C_COMPLETE, S_TODAY, "f.job-vm", 'await Navigator.NavigateRouteAsync(this, "Today"', 'new ("Today"', "Today", "after completing",
      mechanism="INavigator.NavigateRouteAsync", rationale="Complete navigates in code after the upload succeeds.")
route("route.profile-to-settings", "Profile → Settings", I_SETTINGS_BTN, S_SETTINGS, "f.profile-xaml", 'uen:Navigation.Request="Settings"', 'new ("Settings"', "Settings", "click")

# ------------------------------------------------------------------------------------------- graph

graph = {
    "schemaVersion": "0.1",
    "graphId": "fieldwork",
    "name": "Fieldwork",
    "description": ("Hand-authored app graph for a field-service app (Uno Platform, MVUX). Four features, eight screens, "
                    "seven view models (JobViewModel is shared by Job and Signature across two features), two shared components "
                    "with six uses (StatusChip twice on Job) and one unused component, ten routes including a Job ↔ Map cycle, twenty-three states, "
                    "two inferred relationships and one observed state."),
    "entry": S_TODAY,
    "files": list(FILES.values()),
    "nodes": NODES,
    "edges": EDGES,
    "unresolved": [
        "SignaturePad (Fieldwork.Ink) is a third-party control; its internal states are not modelled.",
        "Back navigation other than Signature's \"-\" request is not modelled.",
        "IOutboxStore.HasPendingFor is not in the excerpts, so the unsynced-changes link is inferred at low confidence.",
    ],
}

if __name__ == "__main__":
    out = Path(__file__).resolve().parent.parent / "graph" / "fieldwork.graph.json"
    out.write_text(json.dumps(graph, ensure_ascii=False, indent=1) + "\n", encoding="utf-8")
    from collections import Counter
    print(f"wrote {out.name}: {len(NODES)} nodes, {len(EDGES)} edges, {len(FILES)} files")
    print("  " + ", ".join(f"{k} {v}" for k, v in sorted(Counter(n['type'] for n in NODES).items())))
