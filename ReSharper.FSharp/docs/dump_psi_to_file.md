## Dumping the R# PSI to a file

For some purposes it's helpful to dump the [PSI](https://www.jetbrains.com/help/resharper/sdk/PSI.html) to a file and have a look at it. (Please note, this is about the R# trees, not about the IntelliJ ones)  
There's existing functionality in the `ReSharper.FSharp.sln` solution to help with that.
- Open the file `ReSharper.FSharp\test\data\parsing\_.fs` (switch on `Show All Files` in the Rider Explorer if it's not visible).
- Manipulate the content of the file to your liking. For example:  
![code for psi](./images/screenshot_of_code_for_psi.png)
- Navigate to `ReSharper.FSharp/test/src/FSharp.Tests/Parsing/FSharpParserTest.fs`.
- Execute the test `x.``_``()`.
- It will most likely fail with `“There is no gold file at ...`. But a file with the textual representation of the PSI will appear in `ReSharper.FSharp/test/data/parsing/_.fs.tmp`.  
  The example above would look like this:  
![dumped psi](./images/screenshot_of_dumped_psi.png)