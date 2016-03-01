﻿namespace SymTensor

open System.Reflection
open System.Collections.Generic
open Expr

[<AutoOpen>]
module UExprTypes = 

    // int holds the position of the subuexpr that has the dynamic value
    type UExprRngSpec = RangeSpecT<int>
    type UExprRngsSpec = RangesSpecT<int>

    type IUExtensionOp =
        inherit System.IComparable
        abstract Arity: ArityT with get        

    type ULeafOpT =
        | Identity of SizeSpecT
        | Zeros of ShapeSpecT                   
        | ScalarConst of System.IComparable
        | Var of IVarSpec

    type UUnaryOpT =
        | Negate                        
        | Abs
        | SignT
        | Log
        | Log10                           
        | Exp                           
        | Sin
        | Cos
        | Tan
        | Asin
        | Acos
        | Atan
        | Sinh
        | Cosh
        | Tanh
        | Sqrt
        | Ceil
        | Floor
        | Round
        | Truncate                   
        | Sum                           
        | SumAxis of int                
        | Reshape of ShapeSpecT         
        | DoBroadcast of ShapeSpecT       
        | SwapDim of int * int       
        | StoreToVar of IVarSpec
        | Annotated of Annotation       

    type UBinaryOpT =
        | Add                           
        | Substract                     
        | Multiply                      
        | Divide       
        | Modulo                 
        | Power                         
        | Dot                           
        | TensorProduct                 

    type UNaryOpT =
        | Discard        
        | Subtensor of UExprRngsSpec  
        | ExtensionOp of IUExtensionOp
             

    /// unified op of any arity and type
    type UOpT =
        | ULeafOp of ULeafOpT
        | UUnaryOp of UUnaryOpT
        | UBinaryOp of UBinaryOpT
        | UNaryOp of UNaryOpT

    /// unified expression (combines all arities and types)
    [<StructuralComparison; StructuralEquality; StructuredFormatDisplay("{PrettyString}")>]
    type UExprT = 
        | UExpr of UOpT * TypeNameT * ShapeSpecT * (UExprT list)

        member this.PrettyString =
            match this with
            | UExpr (ULeafOp uop, tn, ss, subs) -> sprintf "%A" uop 
            | UExpr (UUnaryOp uop, tn, ss, subs) -> sprintf "%A (%A)" uop subs.[0]
            | UExpr (UBinaryOp uop, tn, ss, subs) -> sprintf "%A (%A, %A)" uop subs.[0] subs.[1]
            | UExpr (UNaryOp uop, tn, ss, subs) -> sprintf "%A (%A)" uop subs


module UExpr =

    /// converts an expression to a unified expression
    let rec toUExpr (expr: ExprT<'T>) =
        let tn = TypeName typeof<'T>.AssemblyQualifiedName
        let shp = Expr.shapeOf expr

        let leaf uop        = UExpr (ULeafOp uop, tn, shp, [])
        let unary uop a     = UExpr (UUnaryOp uop, tn, shp, [toUExpr a])
        let binary uop a b  = UExpr (UBinaryOp uop, tn, shp, [toUExpr a; toUExpr b])
        let nary uop se     = UExpr (UNaryOp uop, tn, shp, se |> List.map toUExpr)

        match expr with
        | Leaf (Expr.Identity ss)       -> leaf (Identity ss)
        | Leaf (Expr.Zeros ss)          -> leaf (Zeros ss)
        | Leaf (Expr.ScalarConst v)     -> leaf (ScalarConst (box v :?> System.IComparable))
        | Leaf (Expr.Var vs)            -> leaf (Var (vs :> IVarSpec))

        | Unary (Expr.Negate, a)        -> unary Negate a
        | Unary (Expr.Abs, a)           -> unary Abs a
        | Unary (Expr.SignT, a)         -> unary SignT a
        | Unary (Expr.Log, a)           -> unary Log a
        | Unary (Expr.Log10, a)         -> unary Log10 a
        | Unary (Expr.Exp, a)           -> unary Exp a
        | Unary (Expr.Sin, a)           -> unary Sin a
        | Unary (Expr.Cos, a)           -> unary Cos a
        | Unary (Expr.Tan, a)           -> unary Tan a
        | Unary (Expr.Asin, a)          -> unary Asin a
        | Unary (Expr.Acos, a)          -> unary Acos a
        | Unary (Expr.Atan, a)          -> unary Atan a
        | Unary (Expr.Sinh, a)          -> unary Sinh a
        | Unary (Expr.Cosh, a)          -> unary Cosh a
        | Unary (Expr.Tanh, a)          -> unary Tanh a
        | Unary (Expr.Sqrt, a)          -> unary Sqrt a
        | Unary (Expr.Ceil, a)          -> unary Ceil a
        | Unary (Expr.Floor, a)         -> unary Floor a
        | Unary (Expr.Round, a)         -> unary Round a
        | Unary (Expr.Truncate, a)      -> unary Truncate a
        | Unary (Expr.Sum, a)           -> unary Sum a
        | Unary (Expr.SumAxis ax, a)    -> unary (SumAxis ax) a
        | Unary (Expr.Reshape ss, a)    -> unary (Reshape ss) a
        | Unary (Expr.DoBroadcast ss, a)-> unary (DoBroadcast ss) a
        | Unary (Expr.SwapDim (ax1, ax2), a) -> unary (SwapDim (ax1, ax2)) a
        | Unary (Expr.Subtensor sr, a)  ->
            let usr, dynExprs = 
                ([], sr)
                ||> List.mapFold (fun dynExprs rng ->
                    let idx = List.length dynExprs + 1
                    match rng with
                    | RSSymElem e                   -> RSSymElem e,                 dynExprs
                    | RSDynElem e                   -> RSDynElem idx,               dynExprs @ [e]
                    | RSSymStartSymEnd (s, f)       -> RSSymStartSymEnd (s, f),     dynExprs
                    | RSDynStartSymSize (s, f)      -> RSDynStartSymSize (idx, f),  dynExprs @ [s]
                    | RSNewAxis                     -> RSNewAxis,                   dynExprs
                    | RSAll                         -> RSAll,                       dynExprs
                    | RSAllFill                     -> RSAllFill,                   dynExprs)           
            // workaround for: this code causes the code to be less generic...
            //let dynUExprs = dynExprs |> List.map (fun (e: ExprT<int>) -> toUExpr e)
            let dynUExprs = dynExprs |> List.map toUExprForInt               
            UExpr(UNaryOp (Subtensor usr), tn, shp, toUExpr a :: dynUExprs)

        | Unary (Expr.StoreToVar vs, a) -> unary (StoreToVar (vs :> IVarSpec)) a
        | Unary (Expr.Annotated ano, a) -> unary (Annotated ano) a

        | Binary (Expr.Add, a, b)       -> binary Add a b
        | Binary (Expr.Substract, a, b) -> binary Substract a b
        | Binary (Expr.Multiply, a, b)  -> binary Multiply a b                     
        | Binary (Expr.Divide, a, b)    -> binary Divide a b             
        | Binary (Expr.Modulo, a, b)    -> binary Modulo a b          
        | Binary (Expr.Power, a, b)     -> binary Power a b               
        | Binary (Expr.Dot, a, b)       -> binary Dot a b                   
        | Binary (Expr.TensorProduct, a, b) -> binary TensorProduct a b             

        | Nary (Expr.Discard, se)       -> nary Discard se
        | Nary (Expr.ExtensionOp eop, se) -> nary (ExtensionOp (eop :?> IUExtensionOp)) se

    and private toUExprForInt (expr: ExprT<int>) =
        toUExpr expr

    /// converts a unified expression to an expression of (known) type
    let rec toExprOfType (UExpr (uop, tn, ss, subUExprs) as uexpr) : ExprT<'T> =
        if TypeName.ofType<'T> <> tn then
            failwith "UExpr type does not match does function"

        let leaf op    = Expr.Leaf op
        let unary op   = Expr.Unary (op, toExprOfType subUExprs.[0])
        let binary op  = Expr.Binary (op, toExprOfType subUExprs.[0], toExprOfType subUExprs.[1])
        let nary op    = Expr.Nary (op, List.map toExprOfType subUExprs)

        match uop with
        | ULeafOp (Identity ss)             -> leaf (Expr.Identity ss)
        | ULeafOp (Zeros ss)                -> leaf (Expr.Zeros ss)
        | ULeafOp (ScalarConst v)           -> leaf (Expr.ScalarConst (box v :?> 'T))
        | ULeafOp (Var vs)                  -> leaf (Expr.Var (box vs :?> VarSpecT<'T>))

        | UUnaryOp Negate                   -> unary Expr.Negate
        | UUnaryOp Abs                      -> unary Expr.Abs
        | UUnaryOp SignT                    -> unary Expr.SignT
        | UUnaryOp Log                      -> unary Expr.Log
        | UUnaryOp Log10                    -> unary Expr.Log10
        | UUnaryOp Exp                      -> unary Expr.Exp                         
        | UUnaryOp Sin                      -> unary Expr.Sin
        | UUnaryOp Cos                      -> unary Expr.Cos
        | UUnaryOp Tan                      -> unary Expr.Tan
        | UUnaryOp Asin                     -> unary Expr.Asin
        | UUnaryOp Acos                     -> unary Expr.Acos
        | UUnaryOp Atan                     -> unary Expr.Atan
        | UUnaryOp Sinh                     -> unary Expr.Sinh
        | UUnaryOp Cosh                     -> unary Expr.Cosh
        | UUnaryOp Tanh                     -> unary Expr.Tanh
        | UUnaryOp Sqrt                     -> unary Expr.Sqrt
        | UUnaryOp Ceil                     -> unary Expr.Ceil
        | UUnaryOp Floor                    -> unary Expr.Floor
        | UUnaryOp Round                    -> unary Expr.Round
        | UUnaryOp Truncate                 -> unary Expr.Truncate
        | UUnaryOp Sum                      -> unary Expr.Sum                           
        | UUnaryOp (SumAxis a)              -> unary (Expr.SumAxis a)            
        | UUnaryOp (Reshape ss)             -> unary (Expr.Reshape ss)
        | UUnaryOp (DoBroadcast ss)         -> unary (Expr.DoBroadcast ss)
        | UUnaryOp (SwapDim (ax1, ax2))     -> unary (Expr.SwapDim (ax1, ax2))
        | UUnaryOp (StoreToVar vs)          -> unary (Expr.StoreToVar (box vs :?> VarSpecT<'T>))
        | UUnaryOp (Annotated ano)          -> unary (Expr.Annotated ano)

        | UBinaryOp Add                     -> binary Expr.Add                         
        | UBinaryOp Substract               -> binary Expr.Substract                    
        | UBinaryOp Multiply                -> binary Expr.Multiply                     
        | UBinaryOp Divide                  -> binary Expr.Divide             
        | UBinaryOp Modulo                  -> binary Expr.Modulo          
        | UBinaryOp Power                   -> binary Expr.Power               
        | UBinaryOp Dot                     -> binary Expr.Dot                   
        | UBinaryOp TensorProduct           -> binary Expr.TensorProduct     
            
        | UNaryOp Discard                   -> nary Expr.Discard
        | UNaryOp (Subtensor sr)            ->
            let drs = subUExprs |> List.tail |> List.map toExprOfTypeInt
            let rec buildExprRngsSpec (srs: UExprRngsSpec) (drs: ExprT<int> list) =
                match srs, drs with
                | RSSymElem e              :: srs, _         -> RSSymElem e                :: buildExprRngsSpec srs drs
                | RSDynElem _              :: srs, dr :: drs -> RSDynElem dr               :: buildExprRngsSpec srs drs
                | RSSymStartSymEnd (s, f)  :: srs, _         -> RSSymStartSymEnd (s, f)    :: buildExprRngsSpec srs drs
                | RSDynStartSymSize (_, f) :: srs, dr :: drs -> RSDynStartSymSize (dr, f)  :: buildExprRngsSpec srs drs
                | RSNewAxis                :: srs, _         -> RSNewAxis                  :: buildExprRngsSpec srs drs
                | RSAll                    :: srs, _         -> RSAll                      :: buildExprRngsSpec srs drs
                | RSAllFill                :: srs, _         -> RSAllFill                  :: buildExprRngsSpec srs drs
                | _                              , _         -> failwith "invalid unified subtensor spec"
            unary (Expr.Subtensor (buildExprRngsSpec sr drs))

        | UNaryOp (ExtensionOp eop)         -> nary (Expr.ExtensionOp (eop :?> IExtensionOp<'T>))

    and private toExprOfTypeInt uexpr : ExprT<int> =
        toExprOfType uexpr

    type private ToExprOfTypeT =
        static member ToExprOfType<'T> uexpr : ExprT<'T> =
            toExprOfType uexpr

    /// converts a unified expression to an expression of the correct type
    let toExpr (UExpr (_, tn, _, _) as uexpr) =
        let gm = typeof<ToExprOfTypeT>.GetMethod ("ToExprOfType", 
                                                  BindingFlags.NonPublic ||| 
                                                  BindingFlags.Public ||| 
                                                  BindingFlags.Static)
        let m = gm.MakeGenericMethod ([| TypeName.getType tn |])
        m.Invoke(null, [| uexpr |])

    /// the op of the given unified expression
    let inline opOf uexpr =
        match uexpr with UExpr(op, typ, shp, se) -> op

    /// the type of the given unified expression
    let inline typeOf uexpr =
        match uexpr with UExpr(op, TypeName tn, shp, se) -> System.Type.GetType(tn)

    /// the type of the given unified expression
    let inline typenameOf uexpr =
        match uexpr with UExpr(op, typ, shp, se) -> typ

    /// the shape of the given unified expression
    let inline shapeOf uexpr = 
        match uexpr with UExpr(op, typ, shp, se) -> shp

    /// counts how many times subExpr occurs in unified expression uexpr
    let subExprOccurrences uexpr =
        let cnt = Dictionary<UExprT, int>()
        let rec build expr =
            if cnt.ContainsKey(expr) then
                cnt.[expr] <- cnt.[expr] + 1
            else
                cnt.[expr] <- 1

            match expr with
            | UExpr (_, _, _, srcs) ->
                for src in srcs do
                    build src
        build uexpr

        fun subExpr ->
            if cnt.ContainsKey(subExpr) then cnt.[subExpr]
            else 0

