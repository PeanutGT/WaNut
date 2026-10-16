using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WaNut.App.Resources;
using WaNut.App.Services;
using WaNut.Core.Downloads;
using WaNut.Core.Hosting;
using WaNut.Core.Instances;
using WaNut.Core.Modpacks;
using WaNut.Core.Mods;
using WaNut.Core.Players;
using WaNut.Core.Settings;
using WaNut.Core.Versions;
using WaNut.Core.Worlds;

namespace WaNut.App.ViewModels;

public sealed partial class CreateServerViewModel : ObservableObject
{
    private readonly ServerVersionCatalog _catalog;
    private readonly ServerCreator _creator;
    private readonly WorldImporter _importer;
    private readonly ModLibrary _mods;
    private readonly ModpackInstaller _modpacks;
    private readonly InstanceStore _store;
    private readonly IDialogService _dialogs;
    private CancellationTokenSource? _cancellation;

    public CreateServerViewModel(
        ServerVersionCatalog catalog,
        ServerCreator creator,
        WorldImporter importer,
        ModLibrary mods,
        ModpackInstaller modpacks,
        InstanceStore store,
        IDialogService dialogs,
        AppSettingsViewModel app)
    {
        App = app;
        _catalog = catalog;
        _creator = creator;
        _importer = importer;
        _mods = mods;
        _modpacks = modpacks;
        _store = store;
        _dialogs = dialogs;
        Picker = new WorldPickerViewModel(dialogs);
        Picker.SelectionChanged += () =>
        {
            PreselectLoaderForMods();
            PreselectWorldVersion();
            OnWorldChanged();
        };
        Picker.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(WorldPickerViewModel.ModdedAccepted))
            {
                CreateCommand.NotifyCanExecuteChanged();
            }
        };
        _selectedWorldChoice = WorldChoices[0];

        var totalMb = MemoryLimits.TotalPhysicalMb();
        MaxMemoryMb = MemoryLimits.MaxServerMb(totalMb);
        _memoryMb = MemoryLimits.DefaultServerMb(totalMb);
    }

    /// <summary>Raised when the wizard should close; true means a server was created.</summary>
    public event Action<bool>? CloseRequested;

    public ObservableCollection<GameVersion> Versions { get; } = [];

    /// <summary>The wizard's first choice: a fresh world, or one of the user's own.</summary>
    public IReadOnlyList<WorldChoice> WorldChoices { get; } =
    [
        new("new", Text.Get("CreateServer_WorldNew"), Text.Get("CreateServer_WorldNewHint")),
        new("import", Text.Get("CreateServer_WorldImport"), Text.Get("CreateServer_WorldImportHint")),
        new("modpack", Text.Get("CreateServer_WorldModpack"), Text.Get("CreateServer_WorldModpackHint")),
    ];

    public WorldPickerViewModel Picker { get; }

    /// <summary>For the Advanced mode switch.</summary>
    public AppSettingsViewModel App { get; }

    public IReadOnlyList<Option<WorldType>> WorldTypes { get; } =
        Enum.GetValues<WorldType>().Select(t => new Option<WorldType>(t, Text.Get($"CreateServer_WorldType{t}"))).ToList();

    public IReadOnlyList<Option<FlatPreset>> FlatPresets { get; } =
        Enum.GetValues<FlatPreset>().Select(p => new Option<FlatPreset>(p, Text.Get($"CreateServer_FlatPreset{p}"))).ToList();

    /// <summary>Empty means random.</summary>
    [ObservableProperty]
    private string _seed = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSuperflat))]
    [NotifyCanExecuteChangedFor(nameof(CreateCommand))]
    private WorldType _worldType = WorldType.Normal;

    [ObservableProperty]
    private FlatPreset _flatPreset = FlatPreset.Classic;

    /// <summary>Advanced mode: raw generator-settings JSON for a Superflat world, used instead of the preset.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CustomGeneratorInvalid))]
    [NotifyCanExecuteChangedFor(nameof(CreateCommand))]
    private string _customGenerator = string.Empty;

    public bool IsSuperflat => WorldType == WorldType.Superflat;

    public bool CustomGeneratorInvalid => !WorldGeneration.IsValidCustomSettings(CustomGenerator);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsImport), nameof(CanBringInventory), nameof(ShowNewerWorldWarning))]
    [NotifyPropertyChangedFor(nameof(IsModpack), nameof(CanPickTypeAndVersion), nameof(ModpackVersionMissing))]
    [NotifyCanExecuteChangedFor(nameof(CreateCommand))]
    private WorldChoice _selectedWorldChoice;

    /// <summary>Carry the host's single-player inventory and position over to their server player.</summary>
    [ObservableProperty]
    private bool _bringInventory = true;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CreateCommand))]
    private bool _newerWorldAccepted;

    public bool IsImport => SelectedWorldChoice.Id == "import";

    public bool IsModpack => SelectedWorldChoice.Id == "modpack";

    /// <summary>The .mrpack the user picked, once it has been read.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ModpackSummary), nameof(ModpackVersionMissing))]
    [NotifyCanExecuteChangedFor(nameof(CreateCommand))]
    private Modpack? _modpack;

    private string? _modpackPath;

    [ObservableProperty]
    private string? _modpackError;

    public string? ModpackSummary => Modpack is null
        ? null
        : string.Format(
            Text.Get("Modpack_Summary"),
            Modpack.Name,
            Modpack.Version,
            Modpack.MinecraftVersion,
            Text.Get($"CreateServer_Type{Modpack.Type}"),
            Modpack.Files.Count,
            Modpack.SkippedFiles);

    /// <summary>A modpack sets the type and version itself; other choices let the user pick them.</summary>
    public bool CanPickTypeAndVersion => IsIdle && !IsModpack;

    /// <summary>The pack needs a Minecraft version WaNut cannot make a server of.</summary>
    public bool ModpackVersionMissing =>
        IsModpack && Modpack is not null && Versions.Count > 0 && Versions.All(v => v.Id != Modpack.MinecraftVersion);

    public string ModpackVersionMissingText => string.Format(Text.Get("Modpack_VersionMissing"), Modpack?.MinecraftVersion);

    /// <summary>
    /// The seed and terrain only matter for a world the server generates. The custom JSON counts only while
    /// Advanced mode shows it, so switching Advanced mode off never leaves a hidden value in use.
    /// </summary>
    private WorldGeneration? NewWorld() => IsImport
        ? null
        : new WorldGeneration(Seed, WorldType, FlatPreset, IsSuperflat && App.AdvancedMode ? CustomGenerator : string.Empty);

    /// <summary>The inventory belongs to the host, so it needs their name.</summary>
    public bool CanBringInventory => IsImport && OwnerName.Trim().Length > 0 && !OwnerNameInvalid;

    /// <summary>The chosen version is older than the one that last saved the world, which can break it.</summary>
    public bool ShowNewerWorldWarning => IsImport && Picker.IsNewerThan(SelectedVersion?.Id);

    public string NewerWorldWarningText => string.Format(Text.Get("World_NewerWarning"), Picker.Source?.Details.VersionName);

    public IReadOnlyList<ServerTypeOption> ServerTypes { get; } = ServerTypeOption.All();

    [ObservableProperty]
    private ServerTypeOption? _selectedType;

    public int MinMemoryMb => MemoryLimits.MinimumMb;

    public int MaxMemoryMb { get; }

    public int MemoryStepMb => MemoryLimits.StepMb;

    public InstanceMetadata? Created { get; private set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CreateCommand))]
    private string _name = Text.Get("CreateServer_DefaultName");

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CreateCommand))]
    [NotifyPropertyChangedFor(nameof(ShowNewerWorldWarning))]
    private GameVersion? _selectedVersion;

    /// <summary>The host's Minecraft name; empty skips it.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CreateCommand))]
    [NotifyPropertyChangedFor(nameof(OwnerNameInvalid), nameof(CanBringInventory))]
    private string _ownerName = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MemoryText))]
    private int _memoryMb;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CreateCommand))]
    private bool _eulaAccepted;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CreateCommand))]
    [NotifyPropertyChangedFor(nameof(IsIdle), nameof(CanPickTypeAndVersion))]
    private bool _isBusy;

    [ObservableProperty]
    private string? _statusText;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProgressPercent), nameof(IsProgressIndeterminate))]
    private double? _progressFraction;

    [ObservableProperty]
    private string? _errorText;

    public bool IsIdle => !IsBusy;

    public bool OwnerNameInvalid => OwnerName.Trim().Length > 0 && !PlayerLists.IsValidName(OwnerName.Trim());

    public bool IsCreating => _cancellation is not null;

    public double ProgressPercent => (ProgressFraction ?? 0) * 100;

    public bool IsProgressIndeterminate => ProgressFraction is null;

    public string MemoryText => string.Format(Text.Get("CreateServer_MemoryValue"), MemoryMb / 1024.0);

    partial void OnSelectedWorldChoiceChanged(WorldChoice value)
    {
        if (IsImport)
        {
            _ = Picker.ScanOnceAsync();
        }

        ApplyModpack();
    }

    [RelayCommand]
    private async Task PickModpackAsync()
    {
        if (_dialogs.PickModpack() is not { } path)
        {
            return;
        }

        ModpackError = null;
        try
        {
            Modpack = await Task.Run(() => ModpackReader.Read(path));
            _modpackPath = path;
            Name = Modpack.Name;
            ApplyModpack();
        }
        catch (ModpackException e)
        {
            Modpack = null;
            _modpackPath = null;
            ModpackError = ModpackProblemText(e);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Modpack = null;
            _modpackPath = null;
            ModpackError = string.Format(Text.Get("Modpack_ReadFailed"), e.Message);
        }
    }

    private static string ModpackProblemText(ModpackException e) => e.Problem == ModpackProblem.UnsupportedLoader
        ? string.Format(Text.Get("Modpack_Problem_UnsupportedLoader"), e.Detail)
        : Text.Get($"Modpack_Problem_{e.Problem}");

    /// <summary>Switches the type and version to the ones the pack was made for.</summary>
    private void ApplyModpack()
    {
        if (!IsModpack || Modpack is null)
        {
            return;
        }

        var type = ServerTypes.First(t => t.Type == Modpack.Type);
        if (SelectedType != type)
        {
            // The version follows once the new type's list has loaded.
            SelectedType = type;
            return;
        }

        SelectedVersion = Versions.FirstOrDefault(v => v.Id == Modpack.MinecraftVersion) ?? SelectedVersion;
        OnPropertyChanged(nameof(ModpackVersionMissing));
        OnPropertyChanged(nameof(ModpackVersionMissingText));
    }

    partial void OnSelectedVersionChanged(GameVersion? value) => NewerWorldAccepted = false;

    private void OnWorldChanged()
    {
        OnPropertyChanged(nameof(ShowNewerWorldWarning));
        OnPropertyChanged(nameof(NewerWorldWarningText));
        CreateCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// Picks the version that last saved the world. When the list doesn't have it (a snapshot, or a version older
    /// than 1.16.5), the oldest offered version that is not older than the world.
    /// </summary>
    private void PreselectWorldVersion()
    {
        if (Picker.Source?.Details.VersionName is not { } world || Versions.Count == 0)
        {
            return;
        }

        var target = GameFeatures.ParseVersion(world);
        SelectedVersion = Versions.FirstOrDefault(v => v.Id == world)
            ?? Versions.Where(v => GameFeatures.ParseVersion(v.Id) >= target).MinBy(v => GameFeatures.ParseVersion(v.Id))
            ?? SelectedVersion;
    }

    /// <summary>A world played with Fabric, Forge or NeoForge mods gets a server of that type, so its mods can be installed too.</summary>
    private void PreselectLoaderForMods()
    {
        if (Picker.Source?.ModServerType is { } type)
        {
            SelectedType = ServerTypes.First(t => t.Type == type);
        }
    }

    partial void OnSelectedTypeChanged(ServerTypeOption? value)
    {
        Picker.ServerType = value?.Type;
        // A load already running picks up the new type when it finishes.
        if (!LoadVersionsCommand.IsRunning)
        {
            LoadVersionsCommand.Execute(null);
        }
    }

    [RelayCommand]
    private async Task LoadVersionsAsync()
    {
        SelectedType ??= ServerTypes[0];
        IsBusy = true;
        ErrorText = null;
        StatusText = Text.Get("CreateServer_LoadingVersions");
        try
        {
            ServerType type;
            IReadOnlyList<GameVersion> releases;
            do
            {
                type = SelectedType.Type;
                releases = await _catalog.GetVersionsAsync(type);
            }
            while (type != SelectedType.Type);

            // Keep the chosen version when the new type offers it too.
            var previous = SelectedVersion?.Id;
            Versions.Clear();
            foreach (var release in releases)
            {
                Versions.Add(release);
            }

            SelectedVersion = Versions.FirstOrDefault(v => v.Id == previous) ?? Versions.FirstOrDefault();
            if (previous is null && IsImport)
            {
                PreselectWorldVersion();
            }

            ApplyModpack();

            StatusText = null;
        }
        catch (Exception)
        {
            // Offline, Mojang unreachable or an unexpected reply: let the user retry instead of crashing.
            StatusText = null;
            ErrorText = Text.Get("CreateServer_VersionsFailed");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanCreate() =>
        IsIdle && EulaAccepted && SelectedType is not null && SelectedVersion is not null && !string.IsNullOrWhiteSpace(Name) && !OwnerNameInvalid
        && (!IsImport || (Picker.IsAccepted && (!ShowNewerWorldWarning || NewerWorldAccepted)))
        && (!IsModpack || (Modpack is not null && SelectedType.Type == Modpack.Type && SelectedVersion.Id == Modpack.MinecraftVersion))
        && (IsImport || !IsSuperflat || !App.AdvancedMode || !CustomGeneratorInvalid);

    [RelayCommand(CanExecute = nameof(CanCreate))]
    private async Task CreateAsync()
    {
        IsBusy = true;
        ErrorText = null;
        _cancellation = new CancellationTokenSource();
        var progress = new Progress<CreateServerProgress>(ShowProgress);
        var notes = string.Empty;
        try
        {
            Created = await _creator.CreateAsync(
                new CreateServerRequest(Name, SelectedVersion!, MemoryMb, EulaAccepted, SelectedType!.Type, OwnerName, NewWorld(), IsModpack ? Modpack!.LoaderVersion : null),
                progress,
                _cancellation.Token);
            if (IsImport)
            {
                notes = await ImportWorldAsync(Created, _cancellation.Token);
            }
            else if (IsModpack)
            {
                await InstallModpackAsync(Created, _cancellation.Token);
            }
        }
        catch (ModpackException e)
        {
            ErrorText = ModpackProblemText(e);
        }
        catch (OperationCanceledException)
        {
            StatusText = null;
            ProgressFraction = null;
        }
        catch (DownloadVerificationException)
        {
            ErrorText = Text.Get("CreateServer_ChecksumFailed");
        }
        catch (WorldException e)
        {
            ErrorText = WorldMessages.Problem(e);
        }
        catch (Exception ex)
        {
            ErrorText = string.Format(Text.Get("CreateServer_Failed"), ex.Message);
        }
        finally
        {
            _cancellation.Dispose();
            _cancellation = null;
            IsBusy = false;
        }

        // Only after IsCreating is false, so the window does not treat this close as "stop the download".
        if (Created is not null)
        {
            if (notes.Length > 0)
            {
                _dialogs.Inform(notes);
            }

            CloseRequested?.Invoke(true);
        }
    }

    /// <summary>
    /// Copies the picked world into the server that was just created. If that fails, the new server is removed
    /// again: it was made for this world and holds nothing else yet. The mods come after, and their failure keeps
    /// the server.
    /// </summary>
    private async Task<string> ImportWorldAsync(InstanceMetadata created, CancellationToken cancellationToken)
    {
        StatusText = Text.Get("World_Copying");
        ProgressFraction = 0;
        var instanceDir = _store.InstanceDir(created.Id);
        WorldImportResult result;
        try
        {
            var request = new WorldImportRequest(Picker.Source!, created.Type, created.MinecraftVersion, OwnerName.Trim(), BringInventory && CanBringInventory);
            result = await _importer.ImportAsync(instanceDir, request, new Progress<double>(f => ProgressFraction = f), cancellationToken);
        }
        catch
        {
            Created = null;
            try
            {
                Directory.Delete(instanceDir, recursive: true);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // The empty server stays in the list; the user can remove its folder.
            }

            throw;
        }

        var mods = string.Empty;
        if (Picker.WillInstallMods)
        {
            StatusText = Text.Get("World_InstallingMods");
            ProgressFraction = 0;
            mods = await WorldMessages.InstallModsAsync(
                _mods, instanceDir, created.MinecraftVersion, Picker.Source!, new Progress<double>(f => ProgressFraction = f), cancellationToken);
        }

        return WorldMessages.Join(WorldMessages.ImportNotes(result), mods);
    }

    /// <summary>
    /// Puts the pack's files into the server that was just created. If that fails or is stopped, the new server is
    /// removed again: without its mods it would not be the server the user asked for.
    /// </summary>
    private async Task InstallModpackAsync(InstanceMetadata created, CancellationToken cancellationToken)
    {
        StatusText = Text.Get("Modpack_Installing");
        ProgressFraction = 0;
        var instanceDir = _store.InstanceDir(created.Id);
        try
        {
            await _modpacks.InstallAsync(instanceDir, _modpackPath!, Modpack!, new Progress<double>(f => ProgressFraction = f), cancellationToken);
        }
        catch
        {
            Created = null;
            try
            {
                Directory.Delete(instanceDir, recursive: true);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // The half-installed server stays in the list; the user can delete it.
            }

            throw;
        }
    }

    /// <summary>Stops a running creation, or closes the wizard when nothing is being created.</summary>
    [RelayCommand]
    private void Cancel()
    {
        if (IsCreating)
        {
            CancelCreation();
            return;
        }

        CloseRequested?.Invoke(false);
    }

    public void CancelCreation() => _cancellation?.Cancel();

    [RelayCommand]
    private void OpenEula()
    {
        Process.Start(new ProcessStartInfo(EulaFile.EulaUrl.AbsoluteUri) { UseShellExecute = true });
    }

    private void ShowProgress(CreateServerProgress progress)
    {
        StatusText = Text.Get($"CreateServer_Stage_{progress.Stage}");
        ProgressFraction = progress.Fraction;
    }
}

/// <summary>One choice in the wizard's server type list.</summary>
public sealed record ServerTypeOption(ServerType Type, string Title, string Description)
{
    /// <summary>The pixel icon for this type, e.g. "type-paper".</summary>
    public string Scene => $"type-{Type.ToString().ToLowerInvariant()}";

    /// <summary>Every type, in the order the lists show them.</summary>
    public static IReadOnlyList<ServerTypeOption> All() =>
        Enum.GetValues<ServerType>().Select(t => new ServerTypeOption(t, Text.Get($"CreateServer_Type{t}"), Text.Get($"CreateServer_Type{t}Hint"))).ToList();
}
