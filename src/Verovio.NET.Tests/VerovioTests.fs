module Verovio.NET.Tests.VerovioTests

open System
open Expecto
open Verovio.NET
open Verovio.NET.Wasm
open Verovio.NET.Native

// ============================================================================
//  Public-API smoke suite for the `Verovio` module. The Wasm + Native
//  backends are stubs at Phase 03 (every operation throws
//  NotImplementedException); this suite exercises the wrappers' OWN
//  pre-backend validation logic (null / empty input checks, negative
//  index / time guards, etc.) so that the public API's contract is
//  pinned independently of any backend's behaviour.
//
//  Backend-dispatched paths are exercised by Phase 04's snapshot suite
//  once the Wasm backend lands.
// ============================================================================

let private wasmStubTests =
    testList
        "Wasm stub backend"
        [ test "WasmBackend.create returns an IVerovioBackend with name 'Wasm (stub)'" {
              use backend = WasmBackend.create ()
              Expect.equal backend.Name "Wasm (stub)" "Stub identifies itself as Wasm (stub)"
          }

          test "Wrapping a Wasm stub in a Toolkit surfaces the backend name" {
              let backend = WasmBackend.create ()
              use toolkit = Verovio.create backend
              Expect.equal (Verovio.backendName toolkit) "Wasm (stub)" "Toolkit forwards backend name"
          }

          test "Verovio.loadData on a Wasm stub throws NotImplementedException for valid input" {
              let backend = WasmBackend.create ()
              use toolkit = Verovio.create backend

              Expect.throwsT<NotImplementedException>
                  (fun () -> Verovio.loadData toolkit LoadOptions.Default "<mei/>" |> ignore)
                  "Wasm stub must throw NotImplementedException at Phase 03"
          } ]

let private nativeStubTests =
    testList
        "Native stub backend"
        [ test "NativeBackend.create returns an IVerovioBackend with name 'Native (stub)'" {
              use backend = NativeBackend.create ()
              Expect.equal backend.Name "Native (stub)" "Stub identifies itself as Native (stub)"
          }

          test "Verovio.loadData on a Native stub throws NotImplementedException" {
              let backend = NativeBackend.create ()
              use toolkit = Verovio.create backend

              Expect.throwsT<NotImplementedException>
                  (fun () -> Verovio.loadData toolkit LoadOptions.Default "<mei/>" |> ignore)
                  "Native stub must throw NotImplementedException (deferred)"
          } ]

let private inputValidationTests =
    // These tests use a Wasm stub backend, but the assertion is that the
    // `Verovio` module's own pre-backend validation rejects the input
    // BEFORE dispatching, so no NotImplementedException is raised — the
    // module returns `Error _` directly. That contract is what we're
    // pinning here.
    testList
        "Verovio module input validation (pre-backend)"
        [ test "loadData rejects null input with ParseFailed" {
              let backend = WasmBackend.create ()
              use toolkit = Verovio.create backend
              let r = Verovio.loadData toolkit LoadOptions.Default null

              match r with
              | Error(LoadError.ParseFailed _) -> ()
              | Ok() -> failtest "null input must not succeed"
              | Error other -> failtestf "expected ParseFailed; got %A" other
          }

          test "loadData rejects empty input with ParseFailed" {
              let backend = WasmBackend.create ()
              use toolkit = Verovio.create backend
              let r = Verovio.loadData toolkit LoadOptions.Default ""

              match r with
              | Error(LoadError.ParseFailed _) -> ()
              | _ -> failtest "empty input must fail with ParseFailed"
          }

          test "loadData rejects whitespace-only input with ParseFailed" {
              let backend = WasmBackend.create ()
              use toolkit = Verovio.create backend
              let r = Verovio.loadData toolkit LoadOptions.Default "   \n\t  "

              match r with
              | Error(LoadError.ParseFailed _) -> ()
              | _ -> failtest "whitespace input must fail with ParseFailed"
          }

          test "renderToSvg rejects pageNumber < 1 with PageOutOfRange" {
              let backend = WasmBackend.create ()
              use toolkit = Verovio.create backend
              let r = Verovio.renderToSvg toolkit RenderOptions.Default 0

              match r with
              | Error(RenderError.PageOutOfRange(0, 0)) -> ()
              | _ -> failtestf "expected PageOutOfRange(0,0); got %A" r
          }

          test "renderToSvg rejects negative pageNumber with PageOutOfRange" {
              let backend = WasmBackend.create ()
              use toolkit = Verovio.create backend
              let r = Verovio.renderToSvg toolkit RenderOptions.Default -5

              match r with
              | Error(RenderError.PageOutOfRange(-5, 0)) -> ()
              | _ -> failtestf "expected PageOutOfRange(-5,0); got %A" r
          }

          test "getElementsAtTime rejects negative timeMs" {
              let backend = WasmBackend.create ()
              use toolkit = Verovio.create backend
              let r = Verovio.getElementsAtTime toolkit -1

              match r with
              | Error(RenderError.RenderFailed _) -> ()
              | _ -> failtestf "expected RenderFailed for negative time; got %A" r
          }

          test "getMidiValuesForElement rejects empty elementId" {
              let backend = WasmBackend.create ()
              use toolkit = Verovio.create backend
              let r = Verovio.getMidiValuesForElement toolkit ""

              match r with
              | Error(RenderError.RenderFailed _) -> ()
              | _ -> failtestf "expected RenderFailed for empty id; got %A" r
          }

          test "getMidiValuesForElement rejects whitespace elementId" {
              let backend = WasmBackend.create ()
              use toolkit = Verovio.create backend
              let r = Verovio.getMidiValuesForElement toolkit "  "

              match r with
              | Error(RenderError.RenderFailed _) -> ()
              | _ -> failtestf "expected RenderFailed for whitespace id; got %A" r
          } ]

let private disposalTests =
    testList
        "Toolkit disposal"
        [ test "Disposing a toolkit disposes the backend" {
              let backend = WasmBackend.create ()
              let toolkit = Verovio.create backend
              // The stub backend's Dispose is a no-op; we're asserting
              // the wiring compiles + runs cleanly, not the side effect.
              Verovio.dispose toolkit
          }

          test "`use` binding disposes on scope exit" {
              do
                  let backend = WasmBackend.create ()
                  use _toolkit = Verovio.create backend
                  ()
          } ]

[<Tests>]
let allVerovioTests =
    testList "Verovio" [ wasmStubTests; nativeStubTests; inputValidationTests; disposalTests ]
