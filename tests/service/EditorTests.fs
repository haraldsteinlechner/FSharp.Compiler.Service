﻿
// To run the tests in this file:
//
// Technique 1: Compile VisualFSharp.Unittests.dll and run it as a set of unit tests
//
// Technique 2:
//
//   Compile this file as an EXE that has InternalsVisibleTo access into the
//   appropriate DLLs.  This can be the quickest way to get turnaround on updating the tests
//   and capturing large amounts of structured output.
//    cd Debug\net40\bin
//    .\fsc.exe --define:EXE -o VisualFSharp.Unittests.exe -g --optimize- -r .\FSharp.LanguageService.Compiler.dll -r nunit.framework.dll ..\..\..\tests\service\FsUnit.fs ..\..\..\tests\service\Common.fs /delaysign /keyfile:..\..\..\src\fsharp\msft.pubkey ..\..\..\tests\service\EditorTests.fs 
//    .\VisualFSharp.Unittests.exe 
//
// Technique 3: 
// 
//    Use F# Interactive.  This only works for FSHarp.Compiler.Service.dll which has a public API

#if INTERACTIVE
#r "../../bin/v4.5/FSharp.Compiler.Service.dll"
#r "../../packages/NUnit/lib/nunit.framework.dll"
#load "FsUnit.fs"
#load "Common.fs"
#else
module Tests.Service.Editor
#endif

open NUnit.Framework
open FsUnit
open System
open System.IO
open Microsoft.FSharp.Compiler
open Microsoft.FSharp.Compiler.SourceCodeServices
open FSharp.Compiler.Service.Tests.Common

let stringMethods = 
#if DOTNETCORE
    ["Chars"; "CompareTo"; "Contains"; "CopyTo"; "EndsWith"; "Equals";
    "GetHashCode"; "GetType"; "IndexOf";
    "IndexOfAny"; "Insert"; "LastIndexOf"; "LastIndexOfAny";
    "Length"; "PadLeft"; "PadRight"; "Remove"; "Replace"; "Split";
    "StartsWith"; "Substring"; "ToCharArray"; "ToLower"; "ToLowerInvariant";
    "ToString"; "ToUpper"; "ToUpperInvariant"; "Trim"; "TrimEnd"; "TrimStart"]
#else
    ["Chars"; "Clone"; "CompareTo"; "Contains"; "CopyTo"; "EndsWith"; "Equals";
    "GetEnumerator"; "GetHashCode"; "GetType"; "GetTypeCode"; "IndexOf";
    "IndexOfAny"; "Insert"; "IsNormalized"; "LastIndexOf"; "LastIndexOfAny";
    "Length"; "Normalize"; "PadLeft"; "PadRight"; "Remove"; "Replace"; "Split";
    "StartsWith"; "Substring"; "ToCharArray"; "ToLower"; "ToLowerInvariant";
    "ToString"; "ToUpper"; "ToUpperInvariant"; "Trim"; "TrimEnd"; "TrimStart"]
#endif

let input = 
  """
  open System
  
  let foo() = 
    let msg = String.Concat("Hello"," ","world")
    if true then 
      printfn "%s" msg.
  """

[<Test>]
let ``Intro test`` () = 

    // Split the input & define file name
    let inputLines = input.Split('\n')
    let file = "/home/user/Test.fsx"
    let parseResult, typeCheckResults =  parseAndCheckScript(file, input) 
    let identToken = FSharpTokenTag.IDENT
//    let projectOptions = checker.GetProjectOptionsFromScript(file, input) |> Async.RunSynchronously

    // We only expect one reported error. However,
    // on Unix, using filenames like /home/user/Test.fsx gives a second copy of all parse errors due to the
    // way the load closure for scripts is generated. So this returns two identical errors
    (match typeCheckResults.Errors.Length with 1 | 2 -> true | _ -> false)  |> shouldEqual true

    // So we check that the messages are the same
    for msg in typeCheckResults.Errors do 
        printfn "Good! got an error, hopefully with the right text: %A" msg
        msg.Message.Contains("Missing qualification after '.'") |> shouldEqual true

    // Get tool tip at the specified location
    let tip = typeCheckResults.GetToolTipTextAlternate(4, 7, inputLines.[1], ["foo"], identToken) |> Async.RunSynchronously
    (sprintf "%A" tip).Replace("\n","") |> shouldEqual """FSharpToolTipText [Single ("val foo : unit -> unitFull name: Test.foo",None)]"""
    // Get declarations (autocomplete) for a location
    let decls =  typeCheckResults.GetDeclarationListInfo(Some parseResult, 7, 23, inputLines.[6], [], "msg", fun _ -> false)|> Async.RunSynchronously
    [ for item in decls.Items -> item.Name ] |> shouldEqual stringMethods

    // Get overloads of the String.Concat method
    let methods = typeCheckResults.GetMethodsAlternate(5, 27, inputLines.[4], Some ["String"; "Concat"]) |> Async.RunSynchronously

    methods.MethodName  |> shouldEqual "Concat"

    // Print concatenated parameter lists
    [ for mi in methods.Methods do
        yield methods.MethodName , [ for p in mi.Parameters do yield p.Display ] ]
        |> shouldEqual
              [("Concat", ["[<ParamArray>] args: obj []"]);
               ("Concat", ["[<ParamArray>] values: string []"]);
               ("Concat", ["values: Collections.Generic.IEnumerable<'T>"]);
               ("Concat", ["values: Collections.Generic.IEnumerable<string>"]);
               ("Concat", ["arg0: obj"]); ("Concat", ["arg0: obj"; "arg1: obj"]);
               ("Concat", ["str0: string"; "str1: string"]);
               ("Concat", ["arg0: obj"; "arg1: obj"; "arg2: obj"]);
               ("Concat", ["str0: string"; "str1: string"; "str2: string"]);
#if !DOTNETCORE
               ("Concat", ["arg0: obj"; "arg1: obj"; "arg2: obj"; "arg3: obj"]);
#endif               
               ("Concat", ["str0: string"; "str1: string"; "str2: string"; "str3: string"])]

[<Test>]
let ``GetMethodsAsSymbols should return all overloads of a method as FSharpSymbolUse`` () =

    let extractCurriedParams (symbol:FSharpSymbolUse) =
        match symbol.Symbol with
        | :? FSharpMemberOrFunctionOrValue as mvf ->
            [for pg in mvf.CurriedParameterGroups do 
                for (p:FSharpParameter) in pg do 
                    yield p.DisplayName, p.Type.Format (symbol.DisplayContext)]
        | _ -> []

    // Split the input & define file name
    let inputLines = input.Split('\n')
    let file = "/home/user/Test.fsx"
    let parseResult, typeCheckResults =  parseAndCheckScript(file, input)
    let methodsSymbols = typeCheckResults.GetMethodsAsSymbols(5, 27, inputLines.[4], ["String"; "Concat"]) |> Async.RunSynchronously
    match methodsSymbols with
    | Some methods ->
        [ for ms in methods do
            yield ms.Symbol.DisplayName, extractCurriedParams ms ]
        |> List.sortBy (fun (_name, parameters) -> parameters.Length, (parameters |> List.map snd ))
        |> shouldEqual
            [("Concat", [("values", "Collections.Generic.IEnumerable<'T>")]);
             ("Concat", [("values", "Collections.Generic.IEnumerable<string>")]);
             ("Concat", [("arg0", "obj")]);
             ("Concat", [("args", "obj []")]);
             ("Concat", [("values", "string []")]);
             ("Concat", [("arg0", "obj"); ("arg1", "obj")]);
             ("Concat", [("str0", "string"); ("str1", "string")]);
             ("Concat", [("arg0", "obj"); ("arg1", "obj"); ("arg2", "obj")]);
             ("Concat", [("str0", "string"); ("str1", "string"); ("str2", "string")]);
#if !DOTNETCORE
             ("Concat", [("arg0", "obj"); ("arg1", "obj"); ("arg2", "obj"); ("arg3", "obj")]);
#endif
             ("Concat", [("str0", "string"); ("str1", "string"); ("str2", "string"); ("str3", "string")])]
    | None -> failwith "No symbols returned"


let input2 = 
        """
[<System.CLSCompliant(true)>]
let foo(x, y) = 
    let msg = String.Concat("Hello"," ","world")
    if true then 
        printfn "x = %d, y = %d" x y 
        printfn "%s" msg

type C() = 
    member x.P = 1
        """

[<Test>]
let ``Symbols basic test`` () = 

    let file = "/home/user/Test.fsx"
    let untyped2, typeCheckResults2 = parseAndCheckScript(file, input2)

    let partialAssemblySignature = typeCheckResults2.PartialAssemblySignature
    
    partialAssemblySignature.Entities.Count |> shouldEqual 1  // one entity

[<Test>]
let ``Symbols many tests`` () = 

    let file = "/home/user/Test.fsx"
    let untyped2, typeCheckResults2 = parseAndCheckScript(file, input2)

    let partialAssemblySignature = typeCheckResults2.PartialAssemblySignature
    
    partialAssemblySignature.Entities.Count |> shouldEqual 1  // one entity
    let moduleEntity = partialAssemblySignature.Entities.[0]

    moduleEntity.DisplayName |> shouldEqual "Test"

    let classEntity = moduleEntity.NestedEntities.[0]

    let fnVal = moduleEntity.MembersFunctionsAndValues.[0]

    fnVal.Accessibility.IsPublic |> shouldEqual true
    fnVal.Attributes.Count |> shouldEqual 1
    fnVal.CurriedParameterGroups.Count |> shouldEqual 1
    fnVal.CurriedParameterGroups.[0].Count |> shouldEqual 2
    fnVal.CurriedParameterGroups.[0].[0].Name.IsSome |> shouldEqual true
    fnVal.CurriedParameterGroups.[0].[1].Name.IsSome |> shouldEqual true
    fnVal.CurriedParameterGroups.[0].[0].Name.Value |> shouldEqual "x"
    fnVal.CurriedParameterGroups.[0].[1].Name.Value |> shouldEqual "y"
    fnVal.DeclarationLocation.StartLine |> shouldEqual 3
    fnVal.DisplayName |> shouldEqual "foo"
    fnVal.EnclosingEntity.DisplayName |> shouldEqual "Test"
    fnVal.EnclosingEntity.DeclarationLocation.StartLine |> shouldEqual 1
    fnVal.GenericParameters.Count |> shouldEqual 0
    fnVal.InlineAnnotation |> shouldEqual FSharpInlineAnnotation.OptionalInline
    fnVal.IsActivePattern |> shouldEqual false
    fnVal.IsCompilerGenerated |> shouldEqual false
    fnVal.IsDispatchSlot |> shouldEqual false
    fnVal.IsExtensionMember |> shouldEqual false
    fnVal.IsPropertyGetterMethod |> shouldEqual false
    fnVal.IsImplicitConstructor |> shouldEqual false
    fnVal.IsInstanceMember |> shouldEqual false
    fnVal.IsMember |> shouldEqual false
    fnVal.IsModuleValueOrMember |> shouldEqual true
    fnVal.IsMutable |> shouldEqual false
    fnVal.IsPropertySetterMethod |> shouldEqual false
    fnVal.IsTypeFunction |> shouldEqual false

    fnVal.FullType.IsFunctionType |> shouldEqual true // int * int -> unit
    fnVal.FullType.GenericArguments.[0].IsTupleType |> shouldEqual true // int * int 
    let argTy1 = fnVal.FullType.GenericArguments.[0].GenericArguments.[0]

    argTy1.TypeDefinition.DisplayName |> shouldEqual "int" // int

    argTy1.HasTypeDefinition |> shouldEqual true
    argTy1.TypeDefinition.IsFSharpAbbreviation |> shouldEqual true // "int"

    let argTy1b = argTy1.TypeDefinition.AbbreviatedType
    argTy1b.TypeDefinition.Namespace |> shouldEqual (Some "Microsoft.FSharp.Core")
    argTy1b.TypeDefinition.CompiledName |> shouldEqual "int32" 

    let argTy1c = argTy1b.TypeDefinition.AbbreviatedType
    argTy1c.TypeDefinition.Namespace |> shouldEqual (Some "System")
    argTy1c.TypeDefinition.CompiledName |> shouldEqual "Int32" 

    let typeCheckContext = typeCheckResults2.ProjectContext
    
    typeCheckContext.GetReferencedAssemblies() |> List.exists (fun s -> s.FileName.Value.Contains(coreLibAssemblyName)) |> shouldEqual true
    

let input3 = 
  """
let date = System.DateTime.Now.ToString().PadRight(25)
  """

[<Test>]
let ``Expression typing test`` () = 

    // Split the input & define file name
    let inputLines = input3.Split('\n')
    let file = "/home/user/Test.fsx"
    let parseResult, typeCheckResults =  parseAndCheckScript(file, input3) 
    let identToken = FSharpTokenTag.IDENT

    // We only expect one reported error. However,
    // on Unix, using filenames like /home/user/Test.fsx gives a second copy of all parse errors due to the
    // way the load closure for scripts is generated. So this returns two identical errors
    typeCheckResults.Errors.Length |> shouldEqual 0

    // Get declarations (autocomplete) for a location
    //
    // Getting the declarations at columns 42 to 43 with [], "" for the names and residue 
    // gives the results for the string type. 
    // 
    for col in 42..43 do 
        let decls =  typeCheckResults.GetDeclarationListInfo(Some parseResult, 2, col, inputLines.[1], [], "", fun _ -> false)|> Async.RunSynchronously
        let autoCompleteSet = set [ for item in decls.Items -> item.Name ]
        autoCompleteSet |> shouldEqual (set stringMethods)

// The underlying problem is that the parser error recovery doesn't include _any_ information for
// the incomplete member:
//    member x.Test = 

[<Test; Ignore("Currently failing, see #139")>]
let ``Find function from member 1`` () = 
    let input = 
      """
type Test() = 
    let abc a b c = a + b + c
    member x.Test = """ 

    // Split the input & define file name
    let inputLines = input.Split('\n')
    let file = "/home/user/Test.fsx"
    let parseResult, typeCheckResults =  parseAndCheckScript(file, input) 

    let decls = typeCheckResults.GetDeclarationListInfo(Some parseResult, 4, 21, inputLines.[3], [], "", fun _ -> false)|> Async.RunSynchronously
    //decls.Items |> Array.map (fun d -> d.Name) |> printfn "---> decls.Items = %A"
    decls.Items |> Seq.exists (fun d -> d.Name = "abc") |> shouldEqual true

[<Test>]
let ``Find function from member 2`` () = 
    let input = 
      """
type Test() = 
    let abc a b c = a + b + c
    member x.Test = a""" 

    // Split the input & define file name
    let inputLines = input.Split('\n')
    let file = "/home/user/Test.fsx"
    let parseResult, typeCheckResults =  parseAndCheckScript(file, input) 

    let decls = typeCheckResults.GetDeclarationListInfo(Some parseResult, 4, 22, inputLines.[3], [], "", fun _ -> false)|> Async.RunSynchronously
    //decls.Items |> Array.map (fun d -> d.Name) |> printfn "---> decls.Items = %A"
    decls.Items |> Seq.exists (fun d -> d.Name = "abc") |> shouldEqual true
 
[<Test>]
let ``Find function from var`` () = 
    let input = 
      """
type Test() = 
    let abc a b c = a + b + c
    let test = """ 

    // Split the input & define file name
    let inputLines = input.Split('\n')
    let file = "/home/user/Test.fsx"
    let parseResult, typeCheckResults =  parseAndCheckScript(file, input) 

    let decls = typeCheckResults.GetDeclarationListInfo(Some parseResult, 4, 15, inputLines.[3], [], "", fun _ -> false)|> Async.RunSynchronously
    //decls.Items |> Array.map (fun d -> d.Name) |> printfn "---> decls.Items = %A"
    decls.Items |> Seq.exists (fun d -> d.Name = "abc") |> shouldEqual true

[<Test; Ignore("Currently failing, see #139")>]
let ``Symbol based find function from member 1`` () = 
    let input = 
      """
type Test() = 
    let abc a b c = a + b + c
    member x.Test = """ 

    // Split the input & define file name
    let inputLines = input.Split('\n')
    let file = "/home/user/Test.fsx"
    let parseResult, typeCheckResults =  parseAndCheckScript(file, input) 

    let decls = typeCheckResults.GetDeclarationListSymbols(Some parseResult, 4, 21, inputLines.[3], [], "", fun _ -> false)|> Async.RunSynchronously
    //decls |> List.map (fun d -> d.Head.Symbol.DisplayName) |> printfn "---> decls = %A"
    decls |> Seq.exists (fun d -> d.Head.Symbol.DisplayName = "abc") |> shouldEqual true

[<Test>]
let ``Symbol based find function from member 2`` () = 
    let input = 
      """
type Test() = 
    let abc a b c = a + b + c
    member x.Test = a""" 

    // Split the input & define file name
    let inputLines = input.Split('\n')
    let file = "/home/user/Test.fsx"
    let parseResult, typeCheckResults =  parseAndCheckScript(file, input) 

    let decls = typeCheckResults.GetDeclarationListSymbols(Some parseResult, 4, 22, inputLines.[3], [], "", fun _ -> false)|> Async.RunSynchronously
    //decls |> List.map (fun d -> d.Head.Symbol.DisplayName) |> printfn "---> decls = %A"
    decls |> Seq.exists (fun d -> d.Head.Symbol.DisplayName = "abc") |> shouldEqual true

[<Test>]
let ``Symbol based find function from var`` () = 
    let input = 
      """
type Test() = 
    let abc a b c = a + b + c
    let test = """ 

    // Split the input & define file name
    let inputLines = input.Split('\n')
    let file = "/home/user/Test.fsx"
    let parseResult, typeCheckResults =  parseAndCheckScript(file, input) 

    let decls = typeCheckResults.GetDeclarationListSymbols(Some parseResult, 4, 15, inputLines.[3], [], "", fun _ -> false)|> Async.RunSynchronously
    //decls |> List.map (fun d -> d.Head.Symbol.DisplayName) |> printfn "---> decls = %A"
    decls |> Seq.exists (fun d -> d.Head.Symbol.DisplayName = "abc") |> shouldEqual true

[<Test>]
let ``Printf specifiers for regular and verbatim strings`` () = 
    let input = 
      """let os = System.Text.StringBuilder()
let _ = Microsoft.FSharp.Core.Printf.printf "%A" 0
let _ = Printf.printf "%A" 0
let _ = Printf.kprintf (fun _ -> ()) "%A" 1
let _ = Printf.bprintf os "%A" 1
let _ = sprintf "%*d" 1
let _ = sprintf "%7.1f" 1.0
let _ = sprintf "%-8.1e+567" 1.0
let _ = sprintf @"%-5s" "value"
let _ = printfn @"%-A" -10
let _ = printf @"
            %-O" -10
let _ = sprintf "

            %-O" -10
let _ = List.map (sprintf @"%A
                           ")
let _ = (10, 12) ||> sprintf "%A
                              %O"
let _ = sprintf "\n%-8.1e+567" 1.0
let _ = sprintf @"%O\n%-5s" "1" "2" 
let _ = sprintf "%%"
let _ = sprintf " %*%" 2
let _ = sprintf "  %.*%" 2
let _ = sprintf "   %*.1%" 2
let _ = sprintf "    %*s" 10 "hello"
let _ = sprintf "     %*.*%" 2 3
let _ = sprintf "      %*.*f" 2 3 4.5
let _ = sprintf "       %.*f" 3 4.5
let _ = sprintf "        %*.1f" 3 4.5
let _ = sprintf "         %6.*f" 3 4.5
let _ = sprintf "          %6.*%" 3
let _ =  printf "           %a" (fun _ _ -> ()) 2
let _ =  printf "            %*a" 3 (fun _ _ -> ()) 2
"""

    let file = "/home/user/Test.fsx"
    let parseResult, typeCheckResults = parseAndCheckScript(file, input) 

    typeCheckResults.Errors |> shouldEqual [||]
    typeCheckResults.GetFormatSpecifierLocationsAndArity() 
    |> Array.map (fun (range,numArgs) -> range.StartLine, range.StartColumn, range.EndLine, range.EndColumn, numArgs)
    |> shouldEqual [|(2, 45, 2, 46, 1); 
                     (3, 23, 3, 24, 1); 
                     (4, 38, 4, 39, 1); 
                     (5, 27, 5, 28, 1); 
                     (6, 17, 6, 19, 2);
                     (7, 17, 7, 21, 1); 
                     (8, 17, 8, 22, 1);
                     (9, 18, 9, 21, 1); 
                     (10, 18, 10, 20, 1);
                     (12, 12, 12, 14, 1); 
                     (15, 12, 15, 14, 1);
                     (16, 28, 16, 29, 1); 
                     (18, 30, 18, 31, 1);
                     (19, 30, 19, 31, 1);
                     (20, 19, 20, 24, 1); 
                     (21, 18, 21, 19, 1);
                     (21, 22, 21, 25, 1);
                     (22, 17, 22, 18, 0);
                     (23, 18, 23, 20, 1);
                     (24, 19, 24, 22, 1);
                     (25, 20, 25, 24, 1);
                     (26, 21, 26, 23, 2);
                     (27, 22, 27, 26, 2);
                     (28, 23, 28, 27, 3);
                     (29, 24, 29, 27, 2);
                     (30, 25, 30, 29, 2);
                     (31, 26, 31, 30, 2);
                     (32, 27, 32, 31, 1);
                     (33, 28, 33, 29, 2);
                     (34, 29, 34, 31, 3)|]

[<Test>]
let ``Printf specifiers for triple-quote strings`` () = 
    let input = 
      "
let _ = sprintf \"\"\"%-A\"\"\" -10
let _ = printfn \"\"\"
            %-A
                \"\"\" -10
let _ = List.iter(printfn \"\"\"%-A
                             %i\\n%O
                             \"\"\" 1 2)"

    let file = "/home/user/Test.fsx"
    let parseResult, typeCheckResults = parseAndCheckScript(file, input) 

    typeCheckResults.Errors |> shouldEqual [||]
    typeCheckResults.GetFormatSpecifierLocationsAndArity() 
    |> Array.map (fun (range, numArgs) -> range.StartLine, range.StartColumn, range.EndLine, range.EndColumn, numArgs)
    |> shouldEqual [|(2, 19, 2, 21, 1);
                     (4, 12, 4, 14, 1);
                     (6, 29, 6, 31, 1);
                     (7, 29, 7, 30, 1);
                     (7, 33, 7, 34, 1)|]
 
[<Test>]
let ``Printf specifiers for user-defined functions`` () = 
    let input = 
      """
let debug msg = Printf.kprintf System.Diagnostics.Debug.WriteLine msg
let _ = debug "Message: %i - %O" 1 "Ok"
let _ = debug "[LanguageService] Type checking fails for '%s' with content=%A and %A.\nResulting exception: %A" "1" "2" "3" "4"
"""

    let file = "/home/user/Test.fsx"
    let parseResult, typeCheckResults = parseAndCheckScript(file, input) 

    typeCheckResults.Errors |> shouldEqual [||]
    typeCheckResults.GetFormatSpecifierLocationsAndArity() 
    |> Array.map (fun (range, numArgs) -> range.StartLine, range.StartColumn, range.EndLine, range.EndColumn, numArgs)
    |> shouldEqual [|(3, 24, 3, 25, 1); 
                     (3, 29, 3, 30, 1);
                     (4, 58, 4, 59, 1);
                     (4, 75, 4, 76, 1);
                     (4, 82, 4, 83, 1);
                     (4, 108, 4, 109, 1)|]

[<Test>]
let ``should not report format specifiers for illformed format strings`` () = 
    let input = 
      """
let _ = sprintf "%.7f %7.1A %7.f %--8.1f"
let _ = sprintf "ABCDE"
"""

    let file = "/home/user/Test.fsx"
    let parseResult, typeCheckResults = parseAndCheckScript(file, input) 
    typeCheckResults.GetFormatSpecifierLocationsAndArity() 
    |> Array.map (fun (range, numArgs) -> range.StartLine, range.StartColumn, range.EndLine, range.EndColumn, numArgs)
    |> shouldEqual [||]

[<Test>]
let ``Single case discreminated union type definition`` () = 
    let input = 
      """
type DU = Case1
"""

    let file = "/home/user/Test.fsx"
    let parseResult, typeCheckResults = parseAndCheckScript(file, input) 
    typeCheckResults.GetAllUsesOfAllSymbolsInFile()
    |> Async.RunSynchronously
    |> Array.map (fun su -> 
        let r = su.RangeAlternate 
        r.StartLine, r.StartColumn, r.EndLine, r.EndColumn)
    |> shouldEqual [|(2, 10, 2, 15); (2, 5, 2, 7); (1, 0, 1, 0)|]

[<Test>]
let ``Synthetic symbols should not be reported`` () = 
    let input = 
      """
let arr = [|1|]
let number1, number2 = 1, 2
let _ = arr.[0..number1]
let _ = arr.[..number2]
"""

    let file = "/home/user/Test.fsx"
    let parseResult, typeCheckResults = parseAndCheckScript(file, input) 
    typeCheckResults.GetAllUsesOfAllSymbolsInFile()
    |> Async.RunSynchronously
    |> Array.map (fun su -> 
        let r = su.RangeAlternate 
        su.Symbol.ToString(), (r.StartLine, r.StartColumn, r.EndLine, r.EndColumn))
    |> shouldEqual 
        [|("val arr", (2, 4, 2, 7)); 
          ("val number2", (3, 13, 3, 20));
          ("val number1", (3, 4, 3, 11)); 
          ("val arr", (4, 8, 4, 11));
          ("OperatorIntrinsics", (4, 11, 4, 12)); 
          ("Operators", (4, 11, 4, 12));
          ("Core", (4, 11, 4, 12)); 
          ("FSharp", (4, 11, 4, 12));
          ("Microsoft", (4, 11, 4, 12)); 
          ("val number1", (4, 16, 4, 23));
          ("val arr", (5, 8, 5, 11)); 
          ("OperatorIntrinsics", (5, 11, 5, 12));
          ("Operators", (5, 11, 5, 12)); 
          ("Core", (5, 11, 5, 12));
          ("FSharp", (5, 11, 5, 12)); 
          ("Microsoft", (5, 11, 5, 12));
          ("val number2", (5, 15, 5, 22)); 
          ("Test", (1, 0, 1, 0))|]

//-------------------------------------------------------------------------------


#if TEST_TP_PROJECTS
module TPProject = 
    open System.IO

    let fileName1 = Path.ChangeExtension(Path.GetTempFileName(), ".fs")
    let base2 = Path.GetTempFileName()
    let dllName = Path.ChangeExtension(base2, ".dll")
    let projFileName = Path.ChangeExtension(base2, ".fsproj")
    let fileSource1 = """
module M
open Samples.FSharp.RegexTypeProvider
[<Literal>]
let REGEX = "ABC"
let _ = RegexTypedStatic.IsMatch  // TEST: intellisense when typing "<"
let _ = RegexTypedStatic.IsMatch<REGEX>( ) // TEST: param info on "("
let _ = RegexTypedStatic.IsMatch<"ABC" >( ) // TEST: param info on "("
let _ = RegexTypedStatic.IsMatch<"ABC" >( (*$*) ) // TEST: meth info on ctrl-alt-space at $
let _ = RegexTypedStatic.IsMatch<"ABC" >( null (*$*) ) // TEST: param info on "," at $
let _ = RegexTypedStatic.IsMatch< > // TEST: intellisense when typing "<"
let _ = RegexTypedStatic.IsMatch< (*$*) > // TEST: param info when typing ctrl-alt-space at $
let _ = RegexTypedStatic.IsMatch<"ABC" (*$*) > // TEST: param info on Ctrl-alt-space at $
let _ = RegexTypedStatic.IsMatch<"ABC" (*$*) >(  ) // TEST: param info on Ctrl-alt-space at $
let _ = RegexTypedStatic.IsMatch<"ABC", (*$ *) >(  ) // TEST: param info on Ctrl-alt-space at $
let _ = RegexTypedStatic.IsMatch<"ABC" >(  (*$*) ) // TEST: no assert on Ctrl-space at $
    """

    File.WriteAllText(fileName1, fileSource1)
    let fileLines1 = File.ReadAllLines(fileName1)
    let fileNames = [fileName1]
    let args = Array.append (mkProjectCommandLineArgs (dllName, fileNames)) [| "-r:" + PathRelativeToTestAssembly(@"UnitTestsResources\MockTypeProviders\DummyProviderForLanguageServiceTesting.dll") |]
    let internal options =  checker.GetProjectOptionsFromCommandLineArgs (projFileName, args)
    let cleanFileName a = if a = fileName1 then "file1" else "??"

[<Test>]
let ``Test TPProject all symbols`` () = 

    let wholeProjectResults = checker.ParseAndCheckProject(TPProject.options) |> Async.RunSynchronously
    let allSymbolUses = wholeProjectResults.GetAllUsesOfAllSymbols() |> Async.RunSynchronously
    let allSymbolUsesInfo =  [ for s in allSymbolUses -> s.Symbol.DisplayName, tups s.RangeAlternate, attribsOfSymbol s.Symbol ]
    //printfn "allSymbolUsesInfo = \n----\n%A\n----" allSymbolUsesInfo

    allSymbolUsesInfo |> shouldEqual
        [("LiteralAttribute", ((4, 2), (4, 9)), ["class"]);
         ("LiteralAttribute", ((4, 2), (4, 9)), ["class"]);
         ("LiteralAttribute", ((4, 2), (4, 9)), ["member"]);
         ("REGEX", ((5, 4), (5, 9)), ["val"]);
         ("RegexTypedStatic", ((6, 8), (6, 24)), ["class"; "provided"; "erased"]);
         ("IsMatch", ((6, 8), (6, 32)), ["member"]);
         ("RegexTypedStatic", ((7, 8), (7, 24)), ["class"; "provided"; "erased"]);
         ("REGEX", ((7, 33), (7, 38)), ["val"]);
         ("IsMatch", ((7, 8), (7, 32)), ["member"]);
         ("RegexTypedStatic", ((8, 8), (8, 24)), ["class"; "provided"; "erased"]);
         ("IsMatch", ((8, 8), (8, 32)), ["member"]);
         ("RegexTypedStatic", ((9, 8), (9, 24)), ["class"; "provided"; "erased"]);
         ("IsMatch", ((9, 8), (9, 32)), ["member"]);
         ("RegexTypedStatic", ((10, 8), (10, 24)), ["class"; "provided"; "erased"]);
         ("IsMatch", ((10, 8), (10, 32)), ["member"]);
         ("RegexTypedStatic", ((11, 8), (11, 24)), ["class"; "provided"; "erased"]);
         ("IsMatch", ((11, 8), (11, 32)), ["member"]);
         ("RegexTypedStatic", ((12, 8), (12, 24)), ["class"; "provided"; "erased"]);
         ("IsMatch", ((12, 8), (12, 32)), ["member"]);
         ("RegexTypedStatic", ((13, 8), (13, 24)), ["class"; "provided"; "erased"]);
         ("IsMatch", ((13, 8), (13, 32)), ["member"]);
         ("RegexTypedStatic", ((14, 8), (14, 24)), ["class"; "provided"; "erased"]);
         ("IsMatch", ((14, 8), (14, 32)), ["member"]);
         ("RegexTypedStatic", ((15, 8), (15, 24)), ["class"; "provided"; "erased"]);
         ("IsMatch", ((15, 8), (15, 32)), ["member"]);
         ("RegexTypedStatic", ((16, 8), (16, 24)), ["class"; "provided"; "erased"]);
         ("IsMatch", ((16, 8), (16, 32)), ["member"]);
         ("M", ((2, 7), (2, 8)), ["module"])]


[<Test>]
let ``Test TPProject errors`` () = 
    let wholeProjectResults = checker.ParseAndCheckProject(TPProject.options) |> Async.RunSynchronously
    let parseResult, typeCheckAnswer = checker.ParseAndCheckFileInProject(TPProject.fileName1, 0, TPProject.fileSource1, TPProject.options) |> Async.RunSynchronously
    let typeCheckResults = 
        match typeCheckAnswer with
        | FSharpCheckFileAnswer.Succeeded(res) -> res
        | res -> failwithf "Parsing did not finish... (%A)" res

    let errorMessages = [ for msg in typeCheckResults.Errors -> msg.StartLineAlternate, msg.StartColumn, msg.EndLineAlternate, msg.EndColumn, msg.Message.Replace("\r","").Replace("\n","") ]
    //printfn "errorMessages = \n----\n%A\n----" errorMessages

    errorMessages |> shouldEqual
        [(15, 47, 15, 48, "Expected type argument or static argument");
         (6, 8, 6, 32, "This provided method requires static parameters");
         (7, 39, 7, 42, "This expression was expected to have type    'string'    but here has type    'unit'    ");
         (8, 40, 8, 43, "This expression was expected to have type    'string'    but here has type    'unit'    ");
         (9, 40, 9, 49, "This expression was expected to have type    'string'    but here has type    'unit'    ");
         (11, 8, 11, 35, "The static parameter 'pattern1' of the provided type or method 'IsMatch' requires a value. Static parameters to type providers may be optionally specified using named arguments, e.g. 'IsMatch<pattern1=...>'.");
         (12, 8, 12, 41, "The static parameter 'pattern1' of the provided type or method 'IsMatch' requires a value. Static parameters to type providers may be optionally specified using named arguments, e.g. 'IsMatch<pattern1=...>'.");
         (14, 46, 14, 50, "This expression was expected to have type    'string'    but here has type    'unit'    ");
         (15, 33, 15, 38, "No static parameter exists with name ''");
         (16, 40, 16, 50, "This expression was expected to have type    'string'    but here has type    'unit'    ")]

let internal extractToolTipText (FSharpToolTipText(els)) = 
    [ for e in els do 
        match e with
        | FSharpToolTipElement.Single (txt,_) -> yield txt
        | FSharpToolTipElement.Group txts -> for (t,_) in txts do yield t
        | FSharpToolTipElement.CompositionError err -> yield err
        | FSharpToolTipElement.None -> yield "NONE!"
        | FSharpToolTipElement.SingleParameter (txt,p,_) -> yield txt ] 

[<Test>]
let ``Test TPProject quick info`` () = 
    let wholeProjectResults = checker.ParseAndCheckProject(TPProject.options) |> Async.RunSynchronously
    let parseResult, typeCheckAnswer = checker.ParseAndCheckFileInProject(TPProject.fileName1, 0, TPProject.fileSource1, TPProject.options) |> Async.RunSynchronously
    let typeCheckResults = 
        match typeCheckAnswer with
        | FSharpCheckFileAnswer.Succeeded(res) -> res
        | res -> failwithf "Parsing did not finish... (%A)" res

    let toolTips  =
      [ for lineNum in 0 .. TPProject.fileLines1.Length - 1 do 
         let lineText = TPProject.fileLines1.[lineNum]
         if lineText.Contains(".IsMatch") then 
            let colAtEndOfNames = lineText.IndexOf(".IsMatch") + ".IsMatch".Length
            let res = typeCheckResults.GetToolTipTextAlternate(lineNum, colAtEndOfNames, lineText, ["RegexTypedStatic";"IsMatch"], FSharpTokenTag.IDENT) |> Async.RunSynchronously 
            yield lineNum, extractToolTipText  res ]
    //printfn "toolTips = \n----\n%A\n----" toolTips

    toolTips |> shouldEqual
        [(5, ["RegexTypedStatic.IsMatch() : int"]);
         (6, ["RegexTypedStatic.IsMatch() : int"]);
         // NOTE: This tool tip is sub-optimal, it would be better to show RegexTypedStatic.IsMatch<"ABC">
         //       This is a little tricky to implement
         (7, ["RegexTypedStatic.IsMatch,pattern1=\"ABC\"(input: string) : bool"]);
         (8, ["RegexTypedStatic.IsMatch,pattern1=\"ABC\"(input: string) : bool"]);
         (9, ["RegexTypedStatic.IsMatch,pattern1=\"ABC\"(input: string) : bool"]);
         (10, ["RegexTypedStatic.IsMatch,pattern1=\"ABC\"(input: string) : bool"]);
         (11, ["RegexTypedStatic.IsMatch() : int"]);
         (12, ["RegexTypedStatic.IsMatch() : int"]);
         (13, ["RegexTypedStatic.IsMatch,pattern1=\"ABC\"(input: string) : bool"]);
         (14, ["RegexTypedStatic.IsMatch,pattern1=\"ABC\"(input: string) : bool"]);
         (15, ["RegexTypedStatic.IsMatch() : int"])]


[<Test>]
let ``Test TPProject param info`` () = 
    let wholeProjectResults = checker.ParseAndCheckProject(TPProject.options) |> Async.RunSynchronously
    let parseResult, typeCheckAnswer = checker.ParseAndCheckFileInProject(TPProject.fileName1, 0, TPProject.fileSource1, TPProject.options) |> Async.RunSynchronously
    let typeCheckResults = 
        match typeCheckAnswer with
        | FSharpCheckFileAnswer.Succeeded(res) -> res
        | res -> failwithf "Parsing did not finish... (%A)" res

    let paramInfos =
      [ for lineNum in 0 .. TPProject.fileLines1.Length - 1 do 
         let lineText = TPProject.fileLines1.[lineNum]
         if lineText.Contains(".IsMatch") then 
            let colAtEndOfNames = lineText.IndexOf(".IsMatch")  + ".IsMatch".Length
            let meths = typeCheckResults.GetMethodsAlternate(lineNum, colAtEndOfNames, lineText, Some ["RegexTypedStatic";"IsMatch"]) |> Async.RunSynchronously 
            let elems = 
                [ for meth in meths.Methods do 
                   yield extractToolTipText  meth.Description, meth.HasParameters, [ for p in meth.Parameters -> p.ParameterName ], [ for p in meth.StaticParameters -> p.ParameterName ] ]
            yield lineNum, elems]
    //printfn "paramInfos = \n----\n%A\n----" paramInfos 

    // This tests that properly statically-instantiated methods have the right method lists and parameter info
    paramInfos |> shouldEqual
        [(5, [(["RegexTypedStatic.IsMatch() : int"], true, [], ["pattern1"])]);
         (6, [(["RegexTypedStatic.IsMatch() : int"], true, [], ["pattern1"])]);
         // NOTE: this method description is sub-optimal, it would be better to show RegexTypedStatic.IsMatch<"ABC">
         (7,[(["RegexTypedStatic.IsMatch,pattern1=\"ABC\"(input: string) : bool"], true,["input"], ["pattern1"])]);
         (8,[(["RegexTypedStatic.IsMatch,pattern1=\"ABC\"(input: string) : bool"], true,["input"], ["pattern1"])]);
         (9,[(["RegexTypedStatic.IsMatch,pattern1=\"ABC\"(input: string) : bool"], true,["input"], ["pattern1"])]);
         (10,[(["RegexTypedStatic.IsMatch,pattern1=\"ABC\"(input: string) : bool"], true, ["input"], ["pattern1"])]);
         (11, [(["RegexTypedStatic.IsMatch() : int"], true, [], ["pattern1"])]);
         (12, [(["RegexTypedStatic.IsMatch() : int"], true, [], ["pattern1"])]);
         (13,[(["RegexTypedStatic.IsMatch,pattern1=\"ABC\"(input: string) : bool"], true,["input"], ["pattern1"])]);
         (14,[(["RegexTypedStatic.IsMatch,pattern1=\"ABC\"(input: string) : bool"], true,["input"], ["pattern1"])]);
         (15, [(["RegexTypedStatic.IsMatch() : int"], true, [], ["pattern1"])])]

#endif // TEST_TP_PROJECTS

#if EXE

``Intro test`` () 
``Test TPProject all symbols`` () 
``Test TPProject errors`` () 
``Test TPProject quick info`` () 
``Test TPProject param info`` () 
#endif
