﻿namespace Optimizers

open Basics
open ArrayNDNS
open SymTensor


module Adam =

    type Cfg<'T> = {
        Step:           'T
        Momentum:       'T
        Decay:          'T
        DecayMom1:      'T
        DecayMom2:      'T
        Offset:         'T
    } 

    type CfgExpr<'T> = {
        Step:           ExprT<'T>
        Momentum:       ExprT<'T>
        Decay:          ExprT<'T>
        DecayMom1:      ExprT<'T>
        DecayMom2:      ExprT<'T>
        Offset:         ExprT<'T>
    }

    type State<'T> = {
        Iter:           ArrayNDT<'T>  
        LastStep:       ArrayNDT<'T>
        EstMom1:        ArrayNDT<'T>
        EstMom2:        ArrayNDT<'T>
        EstMom1B:       ArrayNDT<'T>
        EstMom2B:       ArrayNDT<'T>
    } 

    type StateExpr<'T> = {
        Iter:           ExprT<'T>  
        LastStep:       ExprT<'T>
        EstMom1:        ExprT<'T>
        EstMom2:        ExprT<'T>
        EstMom1B:       ExprT<'T>
        EstMom2B:       ExprT<'T>
    }

[<AutoOpen>]
module AdamTypes = 
    open Adam

    type Adam<'T when 'T: equality> (loss:  ExprT<'T>,
                                     pars:  ExprT<'T>,
                                     dev:   IDevice) =

        let cfg = {
            CfgExpr.Step        = Expr.var "Adam.Cfg.Step"          []
            CfgExpr.Momentum    = Expr.var "Adam.Cfg.Momentum"      []
            CfgExpr.Decay       = Expr.var "Adam.Cfg.Decay"         []
            CfgExpr.DecayMom1   = Expr.var "Adam.Cfg.DecayMom1"     []
            CfgExpr.DecayMom2   = Expr.var "Adam.Cfg.DecayMom2"     []
            CfgExpr.Offset      = Expr.var "Adam.Cfg.Offset"        []
        }

        let state = {
            StateExpr.Iter      = Expr.var "Adam.State.Iter"        []
            StateExpr.LastStep  = Expr.var "Adam.State.LastStep"    (Expr.shapeOf pars)
            StateExpr.EstMom1   = Expr.var "Adam.State.EstMom1"     (Expr.shapeOf pars)
            StateExpr.EstMom2   = Expr.var "Adam.State.EstMom2"     (Expr.shapeOf pars)
            StateExpr.EstMom1B  = Expr.var "Adam.State.EstMom1B"    (Expr.shapeOf pars)
            StateExpr.EstMom2B  = Expr.var "Adam.State.EstMom2B"    (Expr.shapeOf pars)            
        }

        let rpCfg = VarRecord<Cfg<'T>, CfgExpr<'T>> (cfg, dev)
        let rpState = VarRecord<State<'T>, StateExpr<'T>> (state, dev)

        member this.DefaultCfg : Cfg<'T> = {
            Step        = conv<'T> 2e-4
            Momentum    = conv<'T> 0.0
            Decay       = conv<'T> (1.0 - 1e-8)
            DecayMom1   = conv<'T> 1e-1
            DecayMom2   = conv<'T> 1e-3
            Offset      = conv<'T> 1e-8       
        }

        member this.InitialState (cfg: Cfg<'T>) parVals : State<'T> =
            let shp = ArrayND.shape parVals
            {
                Iter        = ArrayNDHost.zeros []  |> dev.ToDev
                LastStep    = ArrayNDHost.zeros shp |> dev.ToDev
                EstMom1     = ArrayNDHost.zeros shp |> dev.ToDev
                EstMom2     = ArrayNDHost.zeros shp |> dev.ToDev
                EstMom1B    = ArrayNDHost.zeros shp |> dev.ToDev
                EstMom2B    = ArrayNDHost.zeros shp |> dev.ToDev
            }

        member this.Minimize =
            let gradient = Deriv.compute loss |> Deriv.ofVar pars |> Expr.reshape (Expr.shapeOf pars)

            let one, two = Expr.scalart 1, Expr.scalart 2
            let oneHalf = Expr.scalar (conv<'T> 0.5)

            let m, d, o = cfg.Momentum, cfg.Decay, cfg.Offset
            let dm1, dm2 = cfg.DecayMom1, cfg.DecayMom2
            let t = state.Iter + one

            let coeff1 = one - (one - dm1) * d ** (t - one)
            let estMom1B = coeff1 * gradient + (one - coeff1) * state.EstMom1B
            let estMom2B = dm2 * gradient ** two + (one - dm2) * state.EstMom2B
            let estMom1 = estMom1B / (one - (one - dm1) ** t + o)
            let estMom2 = estMom2B / (one - (one - dm2) ** t + o)

            let step1 = cfg.Step * estMom1 / (estMom2 ** oneHalf + o)
            let step2 = m * state.LastStep
            let step = step1 + step2
           
            Expr.discard [
                Expr.storeToVar pars (pars - step)
                Expr.storeToVar state.Iter (state.Iter + one)
                Expr.storeToVar state.LastStep step
                Expr.storeToVar state.EstMom1 estMom1
                Expr.storeToVar state.EstMom2 estMom2
                Expr.storeToVar state.EstMom1B estMom1B
                Expr.storeToVar state.EstMom2B estMom2B
            ]            

        member this.Use f =
            f |> rpState.Use |> rpCfg.Use

        member this.PublishLoc mb =
            rpCfg.PublishLoc mb
            rpState.PublishLoc mb

        interface IOptimizer<'T, Cfg<'T>, State<'T>> with
            member this.OptStepExpr = this.Minimize
            member this.Use f = this.Use f
            member this.CfgWithLearningRate learningRate cfg = {cfg with Step=conv<'T> learningRate}
            member this.InitialState cfg parVals = this.InitialState cfg parVals
            member this.LoadState path = rpState.LoadValue path
            member this.SaveState path state = rpState.SaveValue path state
