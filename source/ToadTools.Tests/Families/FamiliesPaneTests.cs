using Families.Core;
using Families.ViewModels;
using Shouldly;
using ToadTools.Connection;

namespace ToadTools.Tests.Families;

internal sealed class FakeFamiliesApi : IFamiliesApi
{
    public LibraryInfo Library { get; set; } = new(true, @"\\nas\Families", "_На проверке", false);
    public UserInfo Me { get; set; } = new(Guid.NewGuid(), "ivanov", "Иванов", "Designer", []);
    public List<FamilyHit> Hits { get; } = [];
    public List<FamilySearch> Searches { get; } = [];
    public Dictionary<Guid, FamilyCardDto> Cards { get; } = [];
    public List<(Guid Id, string? Hash, string? Project)> Loads { get; } = [];
    public List<ProjectFamilyStatus> Statuses { get; set; } = [];
    public List<IReadOnlyList<ProjectFamilyDto>> StatusRequests { get; } = [];
    public List<PublishRequest> Publications { get; } = [];
    public List<RequestItem> Updates { get; } = [];
    public List<RequestItem> Open { get; } = [];
    public List<Guid> Seen { get; } = [];
    public Exception? Fail { get; set; }

    public Task<UserInfo> MeAsync(CancellationToken ct) => Fail is { } fail ? Task.FromException<UserInfo>(fail) : Task.FromResult(Me);

    public Task<LibraryInfo> LibraryAsync(CancellationToken ct) => Task.FromResult(Library);

    public Task<FamilyPage> SearchAsync(FamilySearch search, int page, CancellationToken ct)
    {
        Searches.Add(search);
        var found = Hits.Where(h => string.IsNullOrWhiteSpace(search.Text) || h.Name.Contains(search.Text!.Trim(), StringComparison.OrdinalIgnoreCase)).ToList();
        return Task.FromResult(new FamilyPage(found.Skip((page - 1) * FamiliesApi.PageSize).Take(FamiliesApi.PageSize).ToList(), found.Count));
    }

    public Task<FamilyFacets> FacetsAsync(bool unreviewed, CancellationToken ct) =>
        Task.FromResult(new FamilyFacets([new FacetValue("ОВ", 3)], [new FacetValue("Арматура", 2)], [], []));

    public Task<FamilyCardDto> CardAsync(Guid id, bool unreviewed, CancellationToken ct) => Task.FromResult(Cards[id]);

    public Task<byte[]> PreviewAsync(Guid id, string hash, CancellationToken ct) => Task.FromResult(new byte[] { 1, 2, 3 });

    public List<Guid?> LoadIterations { get; } = [];
    public List<string> Folders { get; } = [@"ОВ\Клапаны", "КР"];

    public Task ReportLoadAsync(Guid id, string? hash, string? project, Guid? iterationId, CancellationToken ct)
    {
        Loads.Add((id, hash, project));
        LoadIterations.Add(iterationId);
        return Task.CompletedTask;
    }

    public Task<List<string>> FoldersAsync(CancellationToken ct) => Task.FromResult(Folders.ToList());

    public Task<List<ProjectFamilyStatus>> ProjectStatusAsync(IReadOnlyList<ProjectFamilyDto> families, bool unreviewed, CancellationToken ct)
    {
        StatusRequests.Add(families);
        return Task.FromResult(families.Select(f => Statuses.FirstOrDefault(s => s.Name == f.Name) ?? new ProjectFamilyStatus(f.Name, "NotFromLibrary", null, null, null, null, null, null))
            .ToList());
    }

    public Task<PublishResult> PublishAsync(PublishRequest request, CancellationToken ct)
    {
        Publications.Add(request);
        return Task.FromResult(new PublishResult(Guid.NewGuid(), request.Path.Substring(Library.Root.Length + 1), true));
    }

    public Task<List<RequestItem>> RequestUpdatesAsync(CancellationToken ct) => Task.FromResult(Updates.ToList());

    public Task<List<RequestItem>> OpenRequestsAsync(CancellationToken ct) => Task.FromResult(Open.ToList());

    public List<Guid> Accepted { get; } = [];

    public Task AcceptRequestAsync(Guid id, CancellationToken ct)
    {
        Accepted.Add(id);
        return Task.CompletedTask;
    }

    public Task MarkRequestSeenAsync(Guid id, CancellationToken ct)
    {
        Seen.Add(id);
        return Task.CompletedTask;
    }

    public string WebUrl(string relative) => "http://toadflow" + relative;
}

internal sealed class FakeFamilyRevit : IFamilyBridge
{
    public int RevitYear { get; set; } = 2022;
    public ActiveDocument? Document { get; set; } = new("Дом.rvt", false, @"C:\Проекты\Дом.rvt");
    public List<LoadRequest> Loaded { get; } = [];
    public List<ProjectFamily> Families { get; } = [];
    public List<(string Path, bool Overwrite)> Saved { get; } = [];

    /// <summary>Документ был открыт прямо из библиотеки или не сохранён — ушёл в черновики.</summary>
    public bool Redirect { get; set; }

    public Task<ActiveDocument?> ActiveDocumentAsync() => Task.FromResult(Document);

    public Task<LoadOutcome> LoadAsync(LoadRequest request)
    {
        Loaded.Add(request);
        return Task.FromResult(new LoadOutcome(request.Types?.ToList() ?? ["A", "B", "C"], false));
    }

    public Task<IReadOnlyList<ProjectFamily>> ProjectFamiliesAsync() => Task.FromResult<IReadOnlyList<ProjectFamily>>(Families);

    public Task<PublishedCopy> PublishCopyAsync(string path, bool overwrite, string libraryRoot)
    {
        Saved.Add((path, overwrite));
        return Task.FromResult(new PublishedCopy(Redirect ? @"C:\Черновики\" +Path.GetFileName(path) : Document?.PathName ?? "", Redirect));
    }
}

internal sealed class FakeFamilyDialogs : IFamilyDialogs
{
    public bool Answer { get; set; } = true;
    public List<string> Asked { get; } = [];
    public List<string> Urls { get; } = [];
    public List<string> Explorer { get; } = [];

    public bool Confirm(string text)
    {
        Asked.Add(text);
        return Answer;
    }

    public void Info(string text) => Asked.Add(text);

    public void OpenUrl(string url) => Urls.Add(url);

    public void ShowInExplorer(string path) => Explorer.Add(path);
}

public class FamiliesPaneTests
{
    private const string HashA = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string HashB = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";

    private sealed class Setup : IDisposable
    {
        public TempFolder Temp { get; } = new();
        public FakeFamiliesApi Api { get; } = new();
        public FakeFamilyRevit Revit { get; } = new();
        public FakeFamilyDialogs Dialogs { get; } = new();
        public ConnectionStore Store { get; }
        public Dictionary<string, string> FileHashes { get; } = [];
        public FamiliesPaneViewModel Model { get; }

        public Setup(bool connected = true)
        {
            Store = new ConnectionStore(Temp.File("toadflow.json"));
            if (connected) Store.Save(new ToadFlowConnection("http://toadflow", "ivanov", "Иванов", "Designer", Guid.NewGuid(), "tf_x"));
            Model = new FamiliesPaneViewModel(Api, Revit, Store, new PreviewCache(Temp.File("previews"), Api.PreviewAsync), () => true, Dialogs,
                path => FileHashes.TryGetValue(path, out var hash) ? Task.FromResult(hash) : Task.FromException<string>(new IOException("нет файла")));
        }

        public void Dispose() => Temp.Dispose();
    }

    private static FamilyHit Hit(string name, int? year = 2022, string state = "Approved") =>
        new(Guid.NewGuid(), name, "ОВ", $@"ОВ\{name}.rfa", "Арматура", year, 3, state, true, DateTimeOffset.Now, HashA, 0);

    private static FamilyCardDto Card(FamilyHit hit, string hash = HashA, FamilyContentDto? content = null) =>
        new(hit.Id, hit.Name, hit.RelativePath, $@"\\nas\Families\{hit.RelativePath}", "ОВ", hit.State, "Арматура", "OneLevelBased", hit.RevitYear, 3, null, null,
            DateTimeOffset.Now,
            content ?? new FamilyContentDto("Арматура", "OneLevelBased", 2022,
                [new("DN50", new() { ["Ду"] = "50" }), new("DN80", new() { ["Ду"] = "80" }), new("DN100", new() { ["Ду"] = "100" })], false),
            [new FamilyVersionDto(Guid.NewGuid(), hash, hit.RevitYear, "Approved", "", hit.RelativePath, DateTimeOffset.Now, true, "Петров", "первая")], 2, null);

    [Test]
    public async Task NotConnected_AsksToSignIn()
    {
        using var setup = new Setup(connected: false);

        await setup.Model.ActivateAsync();

        setup.Model.Screen.ShouldBe(PaneScreen.NotConnected);
    }

    [Test]
    public async Task LibraryNotConfigured_SaysWhereToSetIt()
    {
        using var setup = new Setup();
        setup.Api.Library = new LibraryInfo(false, "", "_На проверке", false);

        await setup.Model.ActivateAsync();

        setup.Model.Screen.ShouldBe(PaneScreen.NotConfigured);
        setup.Model.Message.ShouldContain("Параметры");
    }

    [Test]
    public async Task ServerDown_SaysFilesAreStillInTheFolder()
    {
        using var setup = new Setup();
        setup.Api.Fail = new ToadFlowUnavailableException("Нет связи с ToadFlow");

        await setup.Model.ActivateAsync();

        setup.Model.Screen.ShouldBe(PaneScreen.Error);
        setup.Model.Message.ShouldContain("проводником");
    }

    [Test]
    public async Task Activate_ShowsFamiliesAndFilters_SearchTextNarrows()
    {
        using var setup = new Setup();
        setup.Api.Hits.AddRange([Hit("Клапан обратный"), Hit("Кран шаровой"), Hit("Клапан R2024", 2024)]);

        await setup.Model.ActivateAsync();
        setup.Model.Items.Count.ShouldBe(3);
        setup.Model.SectionOptions.Select(o => o.Label).ShouldBe(["Все разделы", "ОВ (3)"]);
        setup.Model.Items.Single(i => i.Name == "Клапан R2024").TooNew.ShouldBeTrue();
        setup.Model.SearchText = "клапан";
        await setup.Model.SearchTask;

        setup.Model.Items.Select(i => i.Name).ShouldBe(["Клапан обратный", "Клапан R2024"]);
        setup.Model.Found.ShouldBe("Найдено: 2");
    }

    [Test]
    public async Task Load_ChosenTypes_StampAndLoadReported()
    {
        using var setup = new Setup();
        var hit = Hit("Клапан обратный");
        setup.Api.Hits.Add(hit);
        setup.Api.Cards[hit.Id] = Card(hit);
        setup.FileHashes[$@"\\nas\Families\{hit.RelativePath}"] = HashA;
        await setup.Model.ActivateAsync();

        await setup.Model.OpenCardAsync(hit.Id);
        setup.Model.Card!.Types[2].IsChosen = false;
        await setup.Model.LoadCommand.ExecuteAsync(null);

        var load = setup.Revit.Loaded.Single();
        (load.Path, load.CardId, load.Hash).ShouldBe(($@"\\nas\Families\{hit.RelativePath}", hit.Id, HashA));
        load.Types.ShouldBe(["DN50", "DN80"]);
        setup.Api.Loads.ShouldBe([(hit.Id, (string?)HashA, (string?)"Дом.rvt")]);
        setup.Model.Status.ShouldBe("«Клапан обратный» загружено: 2 типоразмера");
        setup.Dialogs.Asked.ShouldBeEmpty();
    }

    [Test]
    public async Task Load_AllTypesChosen_LoadsWholeFamily()
    {
        using var setup = new Setup();
        var hit = Hit("Клапан");
        setup.Api.Hits.Add(hit);
        setup.Api.Cards[hit.Id] = Card(hit);
        setup.FileHashes[$@"\\nas\Families\{hit.RelativePath}"] = HashA;
        await setup.Model.ActivateAsync();
        await setup.Model.OpenCardAsync(hit.Id);

        await setup.Model.LoadCommand.ExecuteAsync(null);

        setup.Revit.Loaded.Single().Types.ShouldBeNull();
    }

    [Test]
    public async Task Load_FileChangedSinceScan_AsksAndRespectsNo()
    {
        using var setup = new Setup();
        var hit = Hit("Клапан");
        setup.Api.Hits.Add(hit);
        setup.Api.Cards[hit.Id] = Card(hit);
        setup.FileHashes[$@"\\nas\Families\{hit.RelativePath}"] = HashB;
        setup.Dialogs.Answer = false;
        await setup.Model.ActivateAsync();
        await setup.Model.OpenCardAsync(hit.Id);

        await setup.Model.LoadCommand.ExecuteAsync(null);

        setup.Dialogs.Asked.Single().ShouldContain("изменился");
        setup.Revit.Loaded.ShouldBeEmpty();
        setup.Model.Status.ShouldBe("Загрузка отменена");
    }

    [Test]
    public async Task Load_FileChanged_YesLoadsWhatIsInTheFolder()
    {
        using var setup = new Setup();
        var hit = Hit("Клапан");
        setup.Api.Hits.Add(hit);
        setup.Api.Cards[hit.Id] = Card(hit);
        setup.FileHashes[$@"\\nas\Families\{hit.RelativePath}"] = HashB;
        await setup.Model.ActivateAsync();
        await setup.Model.OpenCardAsync(hit.Id);

        await setup.Model.LoadCommand.ExecuteAsync(null);

        setup.Revit.Loaded.Single().Hash.ShouldBe(HashB);
    }

    [Test]
    public async Task Load_FamilyOfNewerRevit_Blocked()
    {
        using var setup = new Setup();
        var hit = Hit("Клапан", 2024);
        setup.Api.Hits.Add(hit);
        setup.Api.Cards[hit.Id] = Card(hit);
        await setup.Model.ActivateAsync();
        setup.Model.Items.Single().TooNew.ShouldBeTrue();

        await setup.Model.OpenCardAsync(hit.Id);
        await setup.Model.LoadCommand.ExecuteAsync(null);

        setup.Model.Card!.CanLoad.ShouldBeFalse();
        setup.Model.Card.BlockedReason.ShouldBe("Сохранено в Revit 2024 — загрузится только в Revit 2024 и новее");
        setup.Revit.Loaded.ShouldBeEmpty();
    }

    [Test]
    public async Task Load_NoProjectOpen_Refused()
    {
        using var setup = new Setup();
        setup.Revit.Document = null;
        var hit = Hit("Клапан");
        setup.Api.Hits.Add(hit);
        setup.Api.Cards[hit.Id] = Card(hit);
        await setup.Model.ActivateAsync();
        await setup.Model.OpenCardAsync(hit.Id);

        await setup.Model.LoadCommand.ExecuteAsync(null);

        setup.Model.Status.ShouldContain("Откройте модель проекта");
        setup.Revit.Loaded.ShouldBeEmpty();
    }

    [Test]
    public async Task NoMatch_RequestOpensWebFormWithSearchAndProjectContext()
    {
        using var setup = new Setup();
        await setup.Model.ActivateAsync();
        setup.Model.SearchText = "решётка АМН";
        await setup.Model.SearchTask;

        setup.Model.RequestFamilyCommand.Execute(null);

        setup.Model.Found.ShouldContain("Ничего не нашлось");
        setup.Dialogs.Urls.Single().ShouldBe("http://toadflow/families/requests/new?q=" + Uri.EscapeDataString("решётка АМН") + "&project=" + Uri.EscapeDataString("Дом") + "&revit=2022");
    }

    [Test]
    public async Task ReviewRequest_AcceptedInPanel_DeclineOpensWeb()
    {
        using var setup = new Setup();
        var review = new RequestItem(Guid.NewGuid(), 12, "Клапан DN50", "Review", "Петров", Guid.NewGuid(), "Клапан", true, DateTimeOffset.Now);
        setup.Api.Updates.Add(review);

        await setup.Model.ActivateAsync();
        setup.Model.RequestNews.ShouldBe("Заявки: на приёмке 1 — подходит?");
        await setup.Model.AcceptRequestCommand.ExecuteAsync(review);
        setup.Model.DeclineRequestCommand.Execute(review);

        setup.Api.Accepted.ShouldBe([review.Id]);
        setup.Dialogs.Urls.Single().ShouldBe($"http://toadflow/families/requests/{review.Id}");
        setup.Model.RequestUpdates.ShouldNotContain(review);
    }

    [Test]
    public async Task Card_ReportProblemOpensFixForm()
    {
        using var setup = new Setup();
        var hit = Hit("Клапан");
        setup.Api.Hits.Add(hit);
        setup.Api.Cards[hit.Id] = Card(hit);
        await setup.Model.ActivateAsync();
        await setup.Model.SearchTask;
        await setup.Model.OpenCardAsync(hit.Id);

        setup.Model.ReportProblemCommand.Execute(null);

        setup.Dialogs.Urls.Single().ShouldBe($"http://toadflow/families/requests/new?kind=fix&card={hit.Id}");
    }

    [Test]
    public async Task ReadyRequest_ShownAndOpensItsFamily()
    {
        using var setup = new Setup();
        var hit = Hit("Клапан");
        setup.Api.Hits.Add(hit);
        setup.Api.Cards[hit.Id] = Card(hit);
        var ready = new RequestItem(Guid.NewGuid(), 12, "Клапан DN50", "Done", "Петров", hit.Id, "Клапан", true, DateTimeOffset.Now);
        setup.Api.Updates.Add(ready);

        await setup.Model.ActivateAsync();
        setup.Model.RequestNews.ShouldBe("Заявки: готово 1");
        await setup.Model.OpenRequestCommand.ExecuteAsync(ready);

        setup.Api.Seen.ShouldBe([ready.Id]);
        setup.Model.Card!.Name.ShouldBe("Клапан");
        setup.Model.RequestNews.ShouldBe("");
    }

    [Test]
    public async Task InProject_StatusesAndUpdateKeepsProjectTypes()
    {
        using var setup = new Setup();
        var card = Guid.NewGuid();
        setup.Revit.Families.AddRange([
            new ProjectFamily("Клапан", "Арматура", card, HashA, ["DN50", "DN80"]),
            new ProjectFamily("Кран", "Арматура", Guid.NewGuid(), HashA, ["Ду15"]),
            new ProjectFamily("Своё", "Обобщённые модели", null, null, ["Тип 1"])
        ]);
        setup.Api.Statuses =
        [
            new ProjectFamilyStatus("Клапан", "Outdated", card, "Клапан", HashB, 2022, @"\\nas\Families\ОВ\Клапан.rfa", DateTimeOffset.Now),
            new ProjectFamilyStatus("Кран", "Current", Guid.NewGuid(), "Кран", HashA, 2022, @"\\nas\Families\ОВ\Кран.rfa", DateTimeOffset.Now)
        ];
        setup.FileHashes[@"\\nas\Families\ОВ\Клапан.rfa"] = HashB;
        await setup.Model.ActivateAsync();

        setup.Model.SelectedTab = 1;
        await setup.Model.RefreshProjectCommand.ExecuteAsync(null);
        setup.Model.ProjectItems.Select(i => (i.Name, i.StateText, i.IsChosen)).ShouldBe([
            ("Клапан", "устарело — в библиотеке новее", true),
            ("Своё", "не из библиотеки", false)
        ]);
        setup.Model.ProjectSummary.ShouldContain("устарели: 1");
        await setup.Model.UpdateChosenCommand.ExecuteAsync(null);

        var load = setup.Revit.Loaded.Single();
        (load.Path, load.CardId, load.Hash).ShouldBe((@"\\nas\Families\ОВ\Клапан.rfa", card, HashB));
        load.Types.ShouldBe(["DN50", "DN80"]);
        setup.Model.Status.ShouldStartWith("Обновлено: 1");
    }

    [Test]
    public async Task Publish_NewFamily_SavedToReviewFolderAndReported()
    {
        using var setup = new Setup();
        setup.Api.Library = setup.Api.Library with { CanPublish = true };
        setup.Revit.Document = new ActiveDocument("Задвижка клиновая.rfa", true, @"C:\Work\Задвижка клиновая.rfa");
        setup.Api.Open.Add(new RequestItem(Guid.NewGuid(), 7, "Задвижка", "InWork", "Петров", null, null, false, DateTimeOffset.Now));
        await setup.Model.ActivateAsync();
        var publish = setup.Model.Publish;
        setup.Model.CanPublish.ShouldBeTrue();
        (publish.Name, publish.Folder).ShouldBe(("Задвижка клиновая", "_На проверке"));
        publish.Comment = "по каталогу";
        publish.Request = publish.Requests[1];
        setup.FileHashes[@"\\nas\Families\_На проверке\Задвижка клиновая.rfa"] = HashA;

        await publish.PublishCommand.ExecuteAsync(null);

        setup.Revit.Saved.ShouldBe([(@"\\nas\Families\_На проверке\Задвижка клиновая.rfa", false)]);
        var sent = setup.Api.Publications.Single();
        (sent.Hash, sent.CardId, sent.Comment, sent.RequestId).ShouldBe((HashA, (Guid?)null, (string?)"по каталогу", setup.Api.Open[0].Id));
        publish.Result.ShouldContain("Опубликовано");
    }

    [Test]
    public async Task Publish_NameTaken_RefusedBeforeSaving()
    {
        using var setup = new Setup();
        setup.Api.Library = setup.Api.Library with { CanPublish = true };
        setup.Revit.Document = new ActiveDocument("Клапан.rfa", true, "");
        setup.Api.Statuses = [new ProjectFamilyStatus("Клапан", "NotFromLibrary", Guid.NewGuid(), "Клапан", HashA, 2022, @"\\nas\Families\ОВ\Клапан.rfa", null)];
        await setup.Model.ActivateAsync();

        await setup.Model.Publish.PublishCommand.ExecuteAsync(null);

        setup.Revit.Saved.ShouldBeEmpty();
        setup.Model.Publish.Result.ShouldContain("уже занято");
    }

    [Test]
    public async Task Publish_NewVersion_GoesToReviewFolderUnderCardName()
    {
        using var setup = new Setup();
        setup.Api.Library = setup.Api.Library with { CanPublish = true };
        setup.Revit.Document = new ActiveDocument("Клапан правка.rfa", true, "");
        var hit = Hit("Клапан");
        setup.Api.Hits.Add(hit);
        await setup.Model.ActivateAsync();
        var publish = setup.Model.Publish;
        publish.IsNewVersion = true;
        publish.ExistingQuery = "Клапан";
        await publish.FindExistingCommand.ExecuteAsync(null);
        setup.FileHashes[@"\\nas\Families\_На проверке\Клапан.rfa"] = HashB;

        await publish.PublishCommand.ExecuteAsync(null);

        setup.Revit.Saved.ShouldBe([(@"\\nas\Families\_На проверке\Клапан.rfa", true)]);
        setup.Api.Publications.Single().CardId.ShouldBe(hit.Id);
    }

    [Test]
    public async Task Publish_FromNewerRevit_WarnsAndCanBeCancelled()
    {
        using var setup = new Setup();
        setup.Api.Library = setup.Api.Library with { CanPublish = true };
        setup.Revit.RevitYear = 2024;
        setup.Revit.Document = new ActiveDocument("Решётка.rfa", true, "");
        setup.Dialogs.Answer = false;
        await setup.Model.ActivateAsync();

        await setup.Model.Publish.PublishCommand.ExecuteAsync(null);

        setup.Model.Publish.YearWarning.ShouldContain("проекты в Revit 2022 его не получат");
        setup.Dialogs.Asked.Single().ShouldContain("Всё равно опубликовать?");
        setup.Revit.Saved.ShouldBeEmpty();
    }

    private static FamilyIterationDto IterationDto(string name, string hash, bool final = false, bool locked = false) =>
        new(Guid.NewGuid(), name, $"для {name}", Guid.NewGuid(), $@"_Итерации\Клапан\{name}\Клапан.rfa", $@"\\nas\Families\_Итерации\Клапан\{name}\Клапан.rfa",
            locked, final, final ? "сдано" : null, null, DateTimeOffset.Now, 2022, hash, null,
            new FamilyContentDto("Арматура", "OneLevelBased", 2022, [new("DN50П", new() { ["Ду"] = "50" })], false));

    /// <summary>Клапан с итерациями «ЖК Полковая» (в работе) и «ЖК Север» (завершена).</summary>
    private static (FamilyHit Hit, FamilyCardDto Card) WithIterations(bool cardLocked = false, bool iterationLocked = false)
    {
        var hit = Hit("Клапан");
        var card = Card(hit) with
        {
            CoordinatorsOnly = cardLocked,
            Iterations = [IterationDto("ЖК Полковая", HashB, locked: iterationLocked), IterationDto("ЖК Север", HashB, final: true)]
        };
        return (hit, card);
    }

    [Test]
    public async Task Load_Iteration_LoadsItsFileAndTypes_StampsIteration()
    {
        using var setup = new Setup();
        var (hit, card) = WithIterations();
        setup.Api.Hits.Add(hit);
        setup.Api.Cards[hit.Id] = card;
        var polk = card.Iterations![0];
        setup.FileHashes[polk.FullPath] = HashB;
        await setup.Model.ActivateAsync();
        await setup.Model.OpenCardAsync(hit.Id);
        var model = setup.Model.Card!;

        model.Variants.Select(v => v.Label).ShouldBe(["Основная", "ЖК Полковая"]);
        model.Select(polk.Id).ShouldBeTrue();
        model.Types.Select(t => t.Name).ShouldBe(["DN50П"]);
        model.VariantNote.ShouldBe("для ЖК Полковая");
        await setup.Model.LoadCommand.ExecuteAsync(null);

        var load = setup.Revit.Loaded.Single();
        (load.Path, load.Hash, load.IterationId).ShouldBe((polk.FullPath, HashB, (Guid?)polk.Id));
        setup.Api.LoadIterations.ShouldBe([(Guid?)polk.Id]);
        setup.Dialogs.Asked.ShouldBeEmpty();
    }

    [Test]
    public async Task Load_FinalIteration_HiddenUntilAsked_ThenConfirmed()
    {
        using var setup = new Setup();
        var (hit, card) = WithIterations();
        setup.Api.Hits.Add(hit);
        setup.Api.Cards[hit.Id] = card;
        var north = card.Iterations![1];
        setup.FileHashes[north.FullPath] = HashB;
        setup.Dialogs.Answer = false;
        await setup.Model.ActivateAsync();
        await setup.Model.OpenCardAsync(hit.Id);
        var model = setup.Model.Card!;

        model.Variants.ShouldNotContain(v => v.IterationId == north.Id);
        model.ShowFinished = true;
        model.Variants.Select(v => v.Label).ShouldContain("ЖК Север (завершена)");
        model.Select(north.Id);
        model.VariantNote.ShouldContain("Итерация завершена: сдано");
        await setup.Model.LoadCommand.ExecuteAsync(null);

        setup.Dialogs.Asked.Single().ShouldContain("завершена");
        setup.Revit.Loaded.ShouldBeEmpty();
        setup.Model.Status.ShouldBe("Загрузка отменена");
    }

    [Test]
    [Arguments(true, false)]
    [Arguments(false, true)]
    public async Task CoordinatorsOnly_DesignerCannotLoad_CoordinatorCan(bool cardLocked, bool iterationLocked)
    {
        using var setup = new Setup();
        var (hit, card) = WithIterations(cardLocked, iterationLocked);
        setup.Api.Hits.Add(hit);
        setup.Api.Cards[hit.Id] = card;
        await setup.Model.ActivateAsync();
        await setup.Model.OpenCardAsync(hit.Id);
        setup.Model.Card!.Select(card.Iterations![0].Id);

        setup.Model.Card.CanLoad.ShouldBeFalse();
        setup.Model.Card.BlockedReason.ShouldBe("Загружают только координаторы");

        setup.Api.Library = setup.Api.Library with { CanPublish = true };
        await setup.Model.ActivateAsync();
        await setup.Model.OpenCardAsync(hit.Id);
        setup.Model.Card!.Select(card.Iterations[0].Id);
        setup.Model.Card.CanLoad.ShouldBeTrue();
    }

    [Test]
    public async Task Project_IterationFromStamp_SentAndUpdatedInSameIteration()
    {
        using var setup = new Setup();
        var cardId = Guid.NewGuid();
        var iterationId = Guid.NewGuid();
        const string path = @"\\nas\Families\_Итерации\Клапан\ЖК Полковая\Клапан.rfa";
        setup.Revit.Families.Add(new ProjectFamily("Клапан", "Арматура", cardId, HashA, ["DN50"], iterationId));
        setup.Api.Statuses = [new ProjectFamilyStatus("Клапан", "Outdated", cardId, "Клапан", HashB, 2022, path, null, iterationId, "ЖК Полковая")];
        setup.FileHashes[path] = HashB;
        await setup.Model.ActivateAsync();
        setup.Model.SelectedTab = 1;
        await setup.Model.RefreshProjectCommand.ExecuteAsync(null);

        setup.Api.StatusRequests.Last().Single().IterationId.ShouldBe(iterationId);
        var row = setup.Model.ProjectItems.Single();
        (row.Iteration, row.StateText).ShouldBe(("ЖК Полковая", "устарело — в итерации новее"));
        await setup.Model.UpdateChosenCommand.ExecuteAsync(null);

        var load = setup.Revit.Loaded.Single();
        (load.Path, load.IterationId).ShouldBe((path, (Guid?)iterationId));
    }

    private static async Task<(Setup Setup, FamilyHit Hit, FamilyCardDto Card)> PublishSetupAsync()
    {
        var setup = new Setup();
        setup.Api.Library = setup.Api.Library with { CanPublish = true, IterationsFolder = "_Итерации" };
        setup.Revit.Document = new ActiveDocument("Клапан правка.rfa", true, @"C:\Work\Клапан правка.rfa");
        var (hit, card) = WithIterations();
        setup.Api.Hits.Add(hit);
        setup.Api.Cards[hit.Id] = card;
        await setup.Model.ActivateAsync();
        var publish = setup.Model.Publish;
        publish.IsIteration = true;
        publish.ExistingQuery = "Клапан";
        await publish.FindExistingCommand.ExecuteAsync(null);
        await publish.LoadIterationsAsync();
        return (setup, hit, card);
    }

    [Test]
    public async Task Publish_IntoExistingIteration_OverwritesItsFile_FinalNeedsComment()
    {
        var (setup, hit, card) = await PublishSetupAsync();
        using var dispose = setup;
        var publish = setup.Model.Publish;
        publish.Iterations.Select(i => i.Name).ShouldBe(["ЖК Полковая", ""]); // завершённой нет, последняя — «новая»
        const string target = @"\\nas\Families\_Итерации\Клапан\ЖК Полковая\Клапан.rfa";
        setup.FileHashes[target] = HashB;
        publish.IsFinal = true;

        await publish.PublishCommand.ExecuteAsync(null);
        publish.Result.ShouldContain("что проделано");
        setup.Revit.Saved.ShouldBeEmpty();

        publish.FinalComment = "сдано, корпус 1";
        publish.Silent = true;
        publish.Comment = "ручка слева";
        await publish.PublishCommand.ExecuteAsync(null);

        setup.Revit.Saved.ShouldBe([(target, true)]);
        var sent = setup.Api.Publications.Single();
        (sent.CardId, sent.IterationId, sent.IterationName, sent.Silent, sent.FinalComment, sent.Comment, sent.RequestId)
            .ShouldBe(((Guid?)hit.Id, (Guid?)card.Iterations![0].Id, (string?)null, true, (string?)"сдано, корпус 1", (string?)"ручка слева", (Guid?)null));
    }

    [Test]
    public async Task Publish_NewIteration_SavedIntoItsFolder_DocumentRedirectedFromLibrary()
    {
        var (setup, hit, _) = await PublishSetupAsync();
        using var dispose = setup;
        var publish = setup.Model.Publish;
        publish.Iteration = publish.Iterations.Last();
        publish.IsNewIteration.ShouldBeTrue();
        publish.NewIterationName = " Дом на Набережной ";
        publish.IterationComment = "корпус 2";
        const string target = @"\\nas\Families\_Итерации\Клапан\Дом на Набережной\Клапан.rfa";
        setup.FileHashes[target] = HashA;
        setup.Revit.Redirect = true;

        await publish.PublishCommand.ExecuteAsync(null);

        setup.Revit.Saved.ShouldBe([(target, false)]);
        var sent = setup.Api.Publications.Single();
        (sent.CardId, sent.IterationId, sent.IterationName, sent.IterationComment).ShouldBe(((Guid?)hit.Id, (Guid?)null, (string?)"Дом на Набережной", (string?)"корпус 2"));
        publish.Result.ShouldContain("Открытый документ теперь сохранён у вас");
    }

    [Test]
    public async Task Publish_NewFamily_FolderChosenFromLibraryFolders()
    {
        using var setup = new Setup();
        setup.Api.Library = setup.Api.Library with { CanPublish = true };
        setup.Revit.Document = new ActiveDocument("Задвижка.rfa", true, @"C:\Work\Задвижка.rfa");
        await setup.Model.ActivateAsync();

        setup.Model.Publish.Folders.ShouldBe(["_На проверке", @"ОВ\Клапаны", "КР"]);
    }

    [Test]
    public async Task Designer_CannotPublish()
    {
        using var setup = new Setup();
        setup.Revit.Document = new ActiveDocument("Решётка.rfa", true, "");

        await setup.Model.ActivateAsync();

        setup.Model.CanPublish.ShouldBeFalse();
    }
}
