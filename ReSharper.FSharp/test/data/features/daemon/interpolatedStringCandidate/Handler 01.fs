module Module

let routef (_: PrintfFormat<_, _, _, _, 'T>) (_: 'T -> int) : int = 1
let _ = routef "%O" int
