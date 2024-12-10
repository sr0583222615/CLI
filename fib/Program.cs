using System;
using System.CommandLine;
using System.CommandLine.Invocation;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;

var rootCommand = new RootCommand("Root command for File bundler CLI");
var bundleCommand = new Command("bundle", "Bundle code files to single file");
rootCommand.AddCommand(bundleCommand);

var bundleOption = new Option<FileInfo>(
    aliases: new[] { "--output", "-o" }, 
    description: "File path and name");

var languageOption = new Option<string>(
    aliases: new[] { "--languages", "-l" }, 
    description: "List of programming languages to include (comma-separated)")
{
    IsRequired = true
};
var sourceOption = new Option<DirectoryInfo>(
    aliases: new[] { "--source", "-s" }, 
    description: "The directory containing the source code files")
{
    IsRequired = true
};
var includeSourceCommentsOption = new Option<bool>(
    aliases:

new[] { "--include-source-comments", "-c" }, 
    description: "Include original file paths as comments in the bundle file");
var sortOption = new Option<string>(
    aliases: new[] { "--sort", "-t" }, 
    getDefaultValue: () => "name", 
    description:

"Sort files by 'name' (default) or 'type' (file extension)");

var removeEmptyLinesOption = new Option<bool>(
    aliases:new[] { "--remove-empty-lines", "-r" }, 
    description: "Remove empty lines from bundled files");

var authorOption = new Option<string>(
    aliases: new[] { "--author", "-a" }, 
    description:

"Write the author on the first line of the bundle file");

bundleCommand.AddOption(bundleOption);
bundleCommand.AddOption(languageOption);
bundleCommand.AddOption(sourceOption);
bundleCommand.AddOption(includeSourceCommentsOption);
bundleCommand.AddOption(sortOption);
bundleCommand.AddOption(removeEmptyLinesOption);
bundleCommand.AddOption(authorOption);

bool IsFileLocked(FileInfo file)
{
    try
    {
        using (var stream = file.Open(FileMode.Open, FileAccess.Read, FileShare.None))
        {
            stream.Close();
        }
        return false;
    }
    catch (IOException)
    {
        return true;
    }
}

bool IsTextFile(FileInfo file)
{
    try
    {
        using (var stream = new FileStream(file.FullName, FileMode.Open, FileAccess.Read))
        using (var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true))
        {
            char[] buffer = new char[512];
            int charsRead = reader.Read(buffer, 0, buffer.Length);
            return buffer.Take(charsRead).All(c => !char.IsControl(c) || char.IsWhiteSpace(c));
        }
    }
    catch
    {
        return false;
    }
}

bundleCommand.SetHandler((FileInfo output, string languages, DirectoryInfo source, bool includeSourceComments, string sort, bool removeEmptyLines, string author) =>
{
    try
    {
        if (!source.Exists)
        {
            Console.WriteLine($"Error: Source directory '{source.FullName}' does not exist.");
            return;
        }

        string directory = output.DirectoryName ?? Directory.GetCurrentDirectory();
        if (!Directory.Exists(directory))
        {
            Console.WriteLine($"Error: The output directory '{directory}' does not exist.");
            return;
        }

        var excludedDirs = new[] { "bin", "debug",".vs","node_modules","log"};
        var files = source.GetFiles("*", SearchOption.AllDirectories)
            .Where(file =>
            {
                bool isExcludedDir = excludedDirs.Any(dir => file.DirectoryName.Contains(dir, StringComparison.OrdinalIgnoreCase));
                bool isHiddenOrSystem = (File.GetAttributes(file.FullName) & (FileAttributes.Hidden | FileAttributes.System)) != 0;
                bool isLocked = IsFileLocked(file);
                bool isText = IsTextFile(file);
                return !isExcludedDir && !isHiddenOrSystem && !isLocked && isText;
            });


        if (!string.Equals(languages, "all", StringComparison.OrdinalIgnoreCase))
        {
            var extensions = languages.Split(',', StringSplitOptions.RemoveEmptyEntries)
                                       .Select(ext => ext.TrimStart('.').ToLowerInvariant());
            files = files.Where(file => extensions.Contains(file.Extension.TrimStart('.').ToLowerInvariant()));
        }

        var fileList = files.ToList();
        if (!fileList.Any())
        {
            Console.WriteLine("No files found matching the specified criteria.");
            return;
        }

        fileList = sort.ToLower() switch
        {
            "type" => fileList.OrderBy(f => f.Extension).ThenBy(f => f.Name).ToList(),
            _ => fileList.OrderBy(f => f.Name).ToList(), // Default: Sort by name
        };

        using var outputFile = new StreamWriter(output.FullName);
        if (author != null)
        {
            outputFile.WriteLine("Bundle created by: " + author);
        }

        foreach (var file in fileList)
        {
            try
            {
                if (includeSourceComments)
                {
                    string relativePath = Path.GetRelativePath(source.FullName, file.FullName);
                    outputFile.WriteLine($"// Source: {relativePath}");
                }

                var content = File.ReadAllText(file.FullName, Encoding.UTF8);
                if (removeEmptyLines)
                {
                    // Remove empty lines
                    content = string.Join("\n", content.Split('\n').Where(line => !string.IsNullOrWhiteSpace(line)));
                }
                outputFile.WriteLine(content);
                outputFile.WriteLine("-----------------------------------------------------------------------------------------------");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Skipped file '{file.FullName}': {ex.Message}");
            }
        }

        Console.WriteLine($"Bundling complete. Output written to '{output.FullName}'");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Error: {ex.Message}");
    }
}, bundleOption, languageOption, sourceOption, includeSourceCommentsOption, sortOption, removeEmptyLinesOption, authorOption);

// Create rsp file command
var createRspCommand = new Command("create-rsp", "Create a response file with bundled command options");
createRspCommand.SetHandler(async (InvocationContext context) =>
{
    try
    { 
    string outputPath;
    string languages;
    string sourcePath;
    bool includeSourceComments;
    string sort;
    bool removeEmptyLines;
    string author;
    while (true)
    {
        Console.Write("Enter output file path: ");
        outputPath = Console.ReadLine();
        if (string.IsNullOrWhiteSpace(outputPath))
        {
            Console.WriteLine("Error: Path cannot be empty.");
            continue;
        }
        var outputFile = new FileInfo(outputPath);
        if (!Directory.Exists(outputFile.DirectoryName))
        {
            Console.WriteLine($"Error: The directory '{outputFile.DirectoryName}' does not exist.");
            continue; 
        }
        if (outputFile.Exists && IsFileLocked(outputFile))
        {
            Console.WriteLine($"Error: The file '{outputFile.FullName}' is locked and cannot be accessed.");
            continue; 
        }
        if (!outputFile.Extension.Equals(".txt", StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine("Error: Output file must end with .txt extension.");
            continue; 
        }
        break;
    }
        while (true)
        {
            Console.Write("Press enter for this directory, or  Enter source directory : ");
            sourcePath = Console.ReadLine();
            if (string.IsNullOrWhiteSpace(sourcePath))
            {
                sourcePath = Directory.GetCurrentDirectory();
                break;
            }
            var sourceDirectory = new DirectoryInfo(sourcePath);
            if (!sourceDirectory.Exists)
            {
                Console.WriteLine($"Error: Source directory '{sourceDirectory.FullName}' does not exist.");
                continue;
            }
            break;
        }
     
        while (true)
        {
            var excludedDirs = new[] { "bin", "debug", ".vs", "node_modules" };
            var excludeFiles = new[] { ".gitignore", ".env" }; // קבצים מסוימים
            var excludeExtensions = new[] { ".log" }; // סיומות מוחרגות
            var files = Directory.GetFiles(sourcePath, "*.*", SearchOption.AllDirectories)
                .Where(file =>
                {
                    string dirPath = Path.GetDirectoryName(file);
                    bool isExcludedDir = excludedDirs.Any(excludedDir =>
                        dirPath.IndexOf(excludedDir, StringComparison.OrdinalIgnoreCase) >= 0);
                    bool isExcludedFile = excludeFiles.Any(excludedFile =>
                        Path.GetFileName(file).Equals(excludedFile, StringComparison.OrdinalIgnoreCase));
                    bool isExcludedExtension = excludeExtensions.Any(excludedExt =>
                        Path.GetExtension(file).Equals(excludedExt, StringComparison.OrdinalIgnoreCase));
                    return !isExcludedDir && !isExcludedFile && !isExcludedExtension;
                })
                .Select(file => Path.GetExtension(file)?.TrimStart('.')) // מסיר את הנקודה מהסיומת
                .Where(ext => !string.IsNullOrEmpty(ext))  // מסנן סיומות ריקות
                .Distinct() // מסנן כפילויות
                .ToArray();

            // הדפסת הסיומות
            Console.WriteLine("File extensions (excluding specific files):");
            foreach (var ext in files)
            {
                Console.WriteLine(ext);
            }

            Console.Write("Enter languages (comma-separated) or enter 'all' :(the languages ​​from the list above ) ");
             languages = Console.ReadLine()?.Trim();
            if (string.IsNullOrWhiteSpace(languages))
            {
                Console.WriteLine("Error: Languages cannot be empty.");
                continue;
            }
            if (languages.Equals("all", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine("Input is valid.");
                break;
            }
            if (languages.Contains(" "))
            {
                Console.WriteLine("Error: Input must not contain spaces. Use comma-separated words only.");
                continue;
            }
            var languagesArray = languages.Split(',', StringSplitOptions.RemoveEmptyEntries);

             var nonMatches = files.Where(num => !languagesArray.Contains(num)).ToArray(); 
              languagesArray = files.Where(num => languagesArray.Contains(num)).ToArray();

            if (nonMatches.Length > 0 && languagesArray.Length == 0) {
                    Console.WriteLine("Error: The languages ​​do not exist in the folder.");
                    continue;
                }

            if (languagesArray.Length == 0 || languagesArray.Any(lang => string.IsNullOrWhiteSpace(lang)))
            {
                Console.WriteLine("Error: Invalid input format. Ensure words are separated by commas and not empty.");
                continue;
            }

            Console.WriteLine("Input is valid.");
            break; 
        }

    while (true)
    {
        Console.Write("Include source comments? if not press n : ");
        string includeSourceCommentsInput = Console.ReadLine();

        if (includeSourceCommentsInput?.ToLower() != "" && includeSourceCommentsInput?.ToLower() != "n")
        {
            Console.WriteLine("Error: Invalid input for source comments. Please enter 'n' or press enter.");
            continue; 
        }
        includeSourceComments = includeSourceCommentsInput?.ToLower() == " ";

        break; 
    }

    while (true)
    {
        Console.Write("Sort by type ? if not press n: ");
        string sortInput = Console.ReadLine();

        if (sortInput?.ToLower() != "" && sortInput?.ToLower() != "n")
        {
            Console.WriteLine("Error: Invalid input for sorting. Please enter  'n' or press  enter.");
            continue; 
        }
        sort = sortInput?.ToLower() == "" ? "type" : "name";

        break; 
    }

    while (true)
    {
        Console.Write("Remove empty lines? if not press n: ");
        string removeEmptyLinesInput = Console.ReadLine();

        if (removeEmptyLinesInput?.ToLower() != "n" && removeEmptyLinesInput?.ToLower() != "")
        {
            Console.WriteLine("Error: Invalid input for removing empty lines. Please enter n or press enter.");
            continue; 
        }
        removeEmptyLines = removeEmptyLinesInput?.ToLower() == "";

        break; 
    }

    Console.Write("Enter author (optional): ");
    author = Console.ReadLine();
        string rspFilePath = Path.Combine(Directory.GetCurrentDirectory(), "args.rsp");
        using var writer = new StreamWriter(rspFilePath);
        writer.WriteLine($"-o \"{outputPath}\"");
        writer.WriteLine($"-l {languages}");
        writer.WriteLine($"-s \"{sourcePath}\"");
        if (includeSourceComments) writer.WriteLine("-c");
        writer.WriteLine($"-t {sort}");
        if (removeEmptyLines) writer.WriteLine("-r");
        if (!string.IsNullOrEmpty(author)) writer.WriteLine($"-a \"{author}\"");

        Console.WriteLine($"Response file 'ars.rsp' created successfully in {Directory.GetCurrentDirectory()}.");
    
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Error: {ex.Message}");
    }
});
rootCommand.AddCommand(createRspCommand);
await rootCommand.InvokeAsync(args);
