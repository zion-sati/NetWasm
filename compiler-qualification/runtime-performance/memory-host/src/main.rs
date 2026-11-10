//! Runs a WASI command component like `wasmtime run <component> <args>`, then
//! reports the peak aggregate linear-memory capacity on stderr as
//! `NETWASM-MEMORY|<bytes>`. It runs a WASI 0.3 command, as NetWasm builds,
//! on the Component Model's async ABI, and a WASI 0.2 command, as Mono's
//! wasi-wasm builds, through the 0.2 bindings.

use std::{env, path::PathBuf, process::ExitCode};

use netwasm_memory_host::CapacityLimiter;
use wasmtime::{
    Config, Engine, Error, Result, Store,
    component::{Component, Linker, ResourceTable},
    error::Context,
};
use wasmtime_wasi::{I32Exit, WasiCtx, WasiCtxView, WasiView};

struct HostState {
    table: ResourceTable,
    wasi: WasiCtx,
    limiter: CapacityLimiter,
}

impl WasiView for HostState {
    fn ctx(&mut self) -> WasiCtxView<'_> {
        WasiCtxView {
            ctx: &mut self.wasi,
            table: &mut self.table,
        }
    }
}

/// Whether the component's command is WASI 0.3's `wasi:cli/run`; otherwise it
/// is WASI 0.2's.
fn exports_wasi_0_3_run(engine: &Engine, component: &Component) -> bool {
    component
        .component_type()
        .exports(engine)
        .any(|(name, _)| name.starts_with("wasi:cli/run@0.3."))
}

/// The guest's exit status for an error: its own status when it called exit.
fn exit_status(error: Error) -> Result<i32> {
    match error.downcast_ref::<I32Exit>() {
        Some(exit) => Ok(exit.0),
        None => Err(error),
    }
}

/// Returns the guest's exit status: zero when `run` succeeds or the guest exits
/// with zero, otherwise the guest's own nonzero status.
async fn run() -> Result<i32> {
    let mut arguments = env::args().skip(1);
    let component_path = PathBuf::from(
        arguments
            .next()
            .ok_or_else(|| Error::msg("usage: netwasm-memory-host <component> [arguments...]"))?,
    );
    let mut configuration = Config::new();
    configuration.wasm_component_model(true);
    configuration.wasm_component_model_async(true);
    configuration.wasm_exceptions(true);
    let engine = Engine::new(&configuration)?;
    let component = Component::from_file(&engine, &component_path)
        .with_context(|| format!("failed to load {}", component_path.display()))?;
    let mut linker = Linker::new(&engine);
    wasmtime_wasi::p2::add_to_linker_async(&mut linker)?;
    wasmtime_wasi::p3::add_to_linker(&mut linker)?;

    let mut wasi = WasiCtx::builder();
    wasi.inherit_stdout().inherit_stderr();
    // The program name is the file name alone: the guest's arguments occupy its
    // linear memory, so a full path would make the peak depend on where the
    // component was built.
    wasi.arg(
        component_path
            .file_name()
            .unwrap_or(component_path.as_os_str())
            .to_string_lossy(),
    );
    for argument in arguments {
        wasi.arg(argument);
    }
    let mut store = Store::new(
        &engine,
        HostState {
            table: ResourceTable::new(),
            wasi: wasi.build(),
            limiter: CapacityLimiter::default(),
        },
    );
    store.limiter(|state| &mut state.limiter);
    let outcome = if exports_wasi_0_3_run(&engine, &component) {
        let command =
            wasmtime_wasi::p3::bindings::Command::instantiate_async(&mut store, &component, &linker)
                .await?;
        store
            .run_concurrent(async move |store| command.wasi_cli_run().call_run(store).await)
            .await
            .and_then(|outcome| outcome)
    } else {
        let command =
            wasmtime_wasi::p2::bindings::Command::instantiate_async(&mut store, &component, &linker)
                .await?;
        command.wasi_cli_run().call_run(&mut store).await
    };
    let status = match outcome {
        Ok(Ok(())) => 0,
        Ok(Err(())) => 1,
        Err(error) => exit_status(error)?,
    };
    eprintln!("NETWASM-MEMORY|{}", store.data().limiter.total_bytes());
    Ok(status)
}

fn main() -> ExitCode {
    let status = tokio::runtime::Builder::new_current_thread()
        .build()
        .map_err(Error::from)
        .and_then(|runtime| runtime.block_on(run()));
    match status {
        Ok(0) => ExitCode::SUCCESS,
        Ok(status) => ExitCode::from(u8::try_from(status).unwrap_or(1).max(1)),
        Err(error) => {
            eprintln!("{error:?}");
            ExitCode::from(2)
        }
    }
}
