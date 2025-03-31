#r "nuget: Fornax.Core, 0.15.1"

open System
open System.IO
open System.Diagnostics

let generate (ctx : SiteContents) (projectRoot: string) (page: string) =
    let generatorDir = projectRoot
    let outputPath = Path.Combine(generatorDir, "_public", Path.GetDirectoryName(page), Path.GetFileName(page))
    
    // Make sure output directory exists
    Directory.CreateDirectory(Path.GetDirectoryName(outputPath)) |> ignore
    
    try
        // Use postcss-cli to process the CSS file
        let psi = new ProcessStartInfo()
        if Environment.OSVersion.Platform = PlatformID.Win32NT then
            psi.FileName <- "cmd"
            psi.Arguments <- sprintf "/c npx postcss %s -o %s" page outputPath
        else
            psi.FileName <- "npx"
            psi.Arguments <- sprintf "postcss %s -o %s" page outputPath
            
        psi.WorkingDirectory <- generatorDir
        psi.UseShellExecute <- false
        psi.RedirectStandardOutput <- true
        psi.RedirectStandardError <- true
        
        // Start the process
        use proc = Process.Start(psi)
        
        // Capture output
        use stdOutReader = proc.StandardOutput
        let cssOutput = stdOutReader.ReadToEnd()
        
        use stdErrReader = proc.StandardError
        let stdErr = stdErrReader.ReadToEnd()
        proc.WaitForExit()
        
        if proc.ExitCode <> 0 then
            printfn "PostCSS process failed with exit code %d" proc.ExitCode
            printfn "Error output: %s" stdErr
            File.ReadAllBytes page // Fallback to the original CSS
        else
            // Read the processed file
            if File.Exists(outputPath) then
                File.ReadAllBytes outputPath
            else
                printfn "Warning: Output file not found at %s" outputPath
                File.ReadAllBytes page // Fallback to the original CSS
    with e ->
        printfn "Error processing CSS: %s" e.Message
        File.ReadAllBytes page // Fallback to the original CSS