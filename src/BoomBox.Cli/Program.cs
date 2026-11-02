using BoomBox.Core;

const string Usage = """
    boombox-cli create <folder> <iso>
    boombox-cli list
    boombox-cli open <path>
    boombox-cli delete <folder>
    """;

try
{
    switch (args)
    {
        case ["create", var folder, var iso]:
            var last = "";
            var progress = new SyncProgress(p =>
            {
                if (p.Stage == last) return;
                last = p.Stage;
                Console.WriteLine(p.Stage);
            });
            var box = await BoxCreator.CreateAsync(folder, iso, progress);
            Console.WriteLine($"{box.OsName} is ready in {box.Folder} (distro {box.Distro}, user {box.User})");
            return 0;

        case ["list"]:
            var states = await Wsl.ListAsync();
            foreach (var item in BoxStore.List())
            {
                Console.WriteLine($"{item.Name,-20} {states.GetValueOrDefault(item.Distro, "Missing"),-10} {item.OsName}  {item.Folder}");
            }
            return 0;

        case ["open", var path]:
            Launcher.OpenPath(path);
            return 0;

        case ["delete", var folder]:
            var target = BoxStore.FindContaining(Path.GetFullPath(folder))
                ?? throw new BoomBoxException("That folder is not a box.");
            await BoxCreator.DeleteAsync(target);
            Console.WriteLine($"Deleted the OS. Your files in {target.Folder} were kept.");
            return 0;

        default:
            Console.Error.WriteLine(Usage);
            return 2;
    }
}
catch (BoomBoxException e)
{
    Console.Error.WriteLine(e.Message);
    return 1;
}

sealed class SyncProgress(Action<BoxProgress> report) : IProgress<BoxProgress>
{
    public void Report(BoxProgress value) => report(value);
}
