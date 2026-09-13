(module
  (import "netwasm.application.v1" "netwasm.filter"
    (func $evaluate_filter (param i32 i32 i32) (result i32)))
  (memory (export "memory") 1)
  (global $heap (mut i32) (i32.const 16))
  (global $exception_frame_top (mut i32) (i32.const 0))
  (global $dispatch_root (mut i32) (i32.const 0))
  (global $dispatch_target_frame (mut i32) (i32.const 0))
  (global $dispatch_target_clause (mut i32) (i32.const 0))
  (global $filter_search_floor (mut i32) (i32.const 0))
  (global $type_object_cache (mut i32) (i32.const 0))
  (global $type_sizes (mut i32) (i32.const 0))
  (global $type_capacity (mut i32) (i32.const 0))

  (func $reserve (param $size i32) (result i32)
    (local $address i32)
    (local $end i32)
    (local $current_size i32)
    local.get $size
    i32.const -16
    i32.gt_u
    if unreachable end
    global.get $heap
    i32.const 15
    i32.add
    i32.const -16
    i32.and
    local.tee $address
    local.get $size
    i32.const 15
    i32.add
    i32.const -16
    i32.and
    i32.add
    local.tee $end
    local.get $address
    i32.lt_u
    if unreachable end
    memory.size
    i32.const 65536
    i32.mul
    local.tee $current_size
    local.get $end
    i32.lt_u
    if
      local.get $end
      local.get $current_size
      i32.sub
      i32.const 1
      i32.sub
      i32.const 16
      i32.shr_u
      i32.const 1
      i32.add
      memory.grow
      i32.const -1
      i32.eq
      if unreachable end
    end
    local.get $end
    global.set $heap
    local.get $address)

  (func (export "initialize")
    (param $static_data_end i32) (param $required_type_capacity i32)
    (param $required_static_root_capacity i32)
    local.get $static_data_end
    i32.const 3
    i32.add
    i32.const -4
    i32.and
    global.set $heap
    local.get $required_type_capacity
    i32.const 1073741823
    i32.gt_u
    if unreachable end
    local.get $required_type_capacity
    global.set $type_capacity
    local.get $required_type_capacity
    i32.const 4
    i32.mul
    call $reserve
    global.set $type_object_cache
    local.get $required_type_capacity
    i32.const 4
    i32.mul
    call $reserve
    global.set $type_sizes)

  ;; The portable runtime preserves the native runtime ABI. A future collector replaces
  ;; these registration no-ops with exact BDWGC descriptors and root ranges.
  (func (export "register_type")
    (param $type_id i32) (param $base_type_id i32) (param $size i32) (param $bitmap i32)
    (param $bit_count i32) (param $has_finalizer i32)
    global.get $type_sizes
    local.get $type_id
    i32.const 2
    i32.shl
    i32.add
    local.get $size
    i32.store)

  (func (export "register_value_type")
    (param $type_id i32) (param $size i32) (param $bitmap i32)
    (param $bit_count i32))

  (func (export "register_static_root") (param $address i32))

  (func (export "suppress_finalize") (param $object i32))
  (func (export "reregister_for_finalize") (param $object i32))

  (func (export "root_frame_enter") (param $slot_count i32) (result i32)
    local.get $slot_count
    i32.const 1073741823
    i32.gt_u
    if unreachable end
    local.get $slot_count
    i32.const 4
    i32.mul
    call $reserve)

  (func (export "root_frame_leave") (param $frame i32))

  ;; Value frames are deliberately outside the exact root shadow stack. Their
  ;; scalar bytes must never be conservatively interpreted as managed roots.
  (func (export "value_frame_enter") (param $byte_count i32) (result i32)
    local.get $byte_count
    call $reserve)

  (func (export "value_frame_leave") (param $frame i32))

  (func $is_assignable (export "is_assignable")
    (param $object i32) (param $target_type_id i32) (result i32)
    local.get $object
    i32.eqz
    if (result i32)
      i32.const 0
    else
      local.get $object
      i32.load
      local.get $target_type_id
      i32.eq
    end)

  ;; Portable EH frames are linked bump allocations. The portable runtime has
  ;; no collector, so releasing a frame only unlinks it.
  (func (export "exception_frame_enter")
    (param $metadata_address i32) (param $clause_count i32) (result i32)
    (local $frame i32)
    i32.const 20
    call $reserve
    local.tee $frame
    global.get $exception_frame_top
    i32.store
    local.get $frame
    local.get $metadata_address
    i32.store offset=4
    local.get $frame
    local.get $clause_count
    i32.const 2147483647
    i32.and
    i32.store offset=8
    local.get $frame
    i32.const 0
    i32.store offset=12
    local.get $frame
    local.get $clause_count
    i32.const 31
    i32.shr_u
    i32.store offset=16
    local.get $frame
    global.set $exception_frame_top
    local.get $frame)

  (func (export "exception_frame_set_environment")
    (param $token i32) (param $environment i32)
    local.get $token
    i32.eqz
    if unreachable end
    local.get $token
    i32.load offset=16
    i32.eqz
    if unreachable end
    local.get $token
    local.get $environment
    i32.store offset=12)

  (func (export "exception_frame_leave") (param $token i32)
    local.get $token
    global.get $exception_frame_top
    i32.ne
    if unreachable end
    local.get $token
    i32.load
    global.set $exception_frame_top)

  (func (export "exception_frame_target_clause")
    (param $token i32) (result i32)
    local.get $token
    global.get $dispatch_target_frame
    i32.eq
    if (result i32)
      global.get $dispatch_target_clause
    else
      i32.const 0
    end)

  ;; The native runtime owns the exception as an exact dispatch root.
  ;; This portable runtime mirrors the two-pass handler search for compiler
  ;; fixtures before the private Wasm tag performs physical unwind.
  (func (export "begin_throw") (param $exception i32)
    (local $token i32)
    (local $metadata i32)
    (local $count i32)
    (local $clause i32)
    (local $frame i32)
    (local $entry i32)
    (local $accepted i32)
    (local $saved_root i32)
    (local $saved_frame i32)
    (local $saved_clause i32)
    (local $saved_floor i32)
    local.get $exception
    global.set $dispatch_root
    i32.const 0
    global.set $dispatch_target_frame
    i32.const 0
    global.set $dispatch_target_clause
    global.get $exception_frame_top
    local.set $token
    block $not_found
      loop $frames
        local.get $token
        global.get $filter_search_floor
        i32.le_u
        br_if $not_found
        local.get $token
        local.tee $frame
        i32.load offset=8
        local.set $count
        local.get $frame
        i32.load offset=4
        local.set $metadata
        i32.const 0
        local.set $clause
        block $next_frame
          loop $clauses
            local.get $clause
            local.get $count
            i32.ge_u
            br_if $next_frame
            local.get $frame
            i32.load offset=16
            if
              local.get $metadata
              local.get $clause
              i32.const 12
              i32.mul
              i32.add
              local.set $entry
            else
              local.get $metadata
              local.get $clause
              i32.const 2
              i32.shl
              i32.add
              local.set $entry
            end
            local.get $frame
            i32.load offset=12
            if (result i32)
              local.get $entry
              i32.load
              i32.eqz
              if (result i32)
                local.get $exception
                local.get $entry
                i32.load offset=4
                call $is_assignable
              else
                global.get $dispatch_root
                local.set $saved_root
                global.get $dispatch_target_frame
                local.set $saved_frame
                global.get $dispatch_target_clause
                local.set $saved_clause
                global.get $filter_search_floor
                local.set $saved_floor
                local.get $token
                global.set $filter_search_floor
                local.get $entry
                i32.load offset=4
                local.get $exception
                local.get $frame
                i32.load offset=12
                call $evaluate_filter
                local.set $accepted
                local.get $saved_root
                global.set $dispatch_root
                local.get $saved_frame
                global.set $dispatch_target_frame
                local.get $saved_clause
                global.set $dispatch_target_clause
                local.get $saved_floor
                global.set $filter_search_floor
                local.get $accepted
              end
            else
              local.get $exception
              local.get $entry
              i32.load
              call $is_assignable
            end
            if
              local.get $token
              global.set $dispatch_target_frame
              local.get $clause
              i32.const 1
              i32.add
              global.set $dispatch_target_clause
              return
            end
            local.get $clause
            i32.const 1
            i32.add
            local.set $clause
            br $clauses
          end
        end
        local.get $frame
        i32.load
        local.set $token
        br $frames
      end
    end)

  (func (export "end_catch")
    i32.const 0
    global.set $dispatch_root
    i32.const 0
    global.set $dispatch_target_frame
    i32.const 0
    global.set $dispatch_target_clause)

  (func (export "exception_get_active") (result i32)
    global.get $dispatch_root)

  (func (export "exception_get_active_type_id") (result i32)
    (local $exception i32)
    global.get $dispatch_root
    local.tee $exception
    i32.eqz
    if (result i32)
      i32.const 0
    else
      local.get $exception
      i32.load
    end)

  (func (export "exception_clear_active")
    i32.const 0
    global.set $dispatch_root
    i32.const 0
    global.set $dispatch_target_frame
    i32.const 0
    global.set $dispatch_target_clause)

  (func (export "finalizer_safepoint") (result i32)
    i32.const 0)

  (func $allocate (export "allocate") (param $size i32) (param $type_id i32) (result i32)
    (local $object i32)
    (local $end i32)

    local.get $size
    call $reserve
    local.set $object
    local.get $object
    local.get $type_id
    i32.store
    local.get $object)

  ;; The portable runtime has no collector, so its canonical table remains
  ;; populated for the lifetime of the instance. The native runtime uses
  ;; disappearing links and therefore does not strongly root these objects.
  (func (export "get_type_object")
    (param $semantic_type_id i32) (param $facade_type_id i32) (result i32)
    (local $slot i32)
    (local $object i32)
    local.get $semantic_type_id
    i32.eqz
    if unreachable end
    local.get $semantic_type_id
    global.get $type_capacity
    i32.ge_u
    if unreachable end
    global.get $type_object_cache
    local.get $semantic_type_id
    i32.const 4
    i32.mul
    i32.add
    local.tee $slot
    i32.load
    local.tee $object
    if
      local.get $object
      return
    end
    i32.const 8
    local.get $facade_type_id
    call $allocate
    local.tee $object
    i32.eqz
    if
      i32.const 0
      return
    end
    local.get $object
    local.get $semantic_type_id
    i32.store offset=4
    local.get $slot
    local.get $object
    i32.store
    local.get $object)

  (func (export "allocate_reference_array")
    (param $length i32) (param $type_id i32) (param $element_type_id i32) (result i32)
    (local $array i32)
    (local $elements i32)
    local.get $length
    i32.const 0
    i32.lt_s
    if
      unreachable
    end
    local.get $length
    i32.const 1073741823
    i32.gt_u
    if
      i32.const 0
      return
    end
    local.get $length
    i32.eqz
    if (result i32)
      i32.const 4
    else
      local.get $length
      i32.const 4
      i32.mul
    end
    i32.const 0
    call $allocate
    local.set $elements
    i32.const 16
    local.get $type_id
    call $allocate
    local.tee $array
    local.get $length
    i32.store offset=4
    local.get $array
    local.get $elements
    i32.store offset=8
    local.get $array
    local.get $element_type_id
    i32.store offset=12
    local.get $array)

  (func (export "allocate_value_array")
    (param $length i32) (param $type_id i32) (param $element_type_id i32)
    (param $element_size i32)
    (result i32)
    (local $array i32)
    (local $elements i32)
    local.get $length
    i32.const 0
    i32.lt_s
    if unreachable end
    local.get $element_size
    if
      local.get $length
      i32.const -4
      local.get $element_size
      i32.div_u
      i32.gt_u
      if
        i32.const 0
        return
      end
    end
    ;; The portable runtime is a compiler fixture. Production native code uses
    ;; the registered exact per-element descriptor and canonical element size.
    local.get $length
    local.get $element_size
    i32.mul
    i32.const 0
    call $allocate
    local.set $elements
    i32.const 16
    local.get $type_id
    call $allocate
    local.tee $array
    local.get $length
    i32.store offset=4
    local.get $array
    local.get $elements
    i32.store offset=8
    local.get $array
    local.get $element_type_id
    i32.store offset=12
    local.get $array)

  (func (export "allocate_rectangular_array")
    (param $rank i32) (param $dimensions i32) (param $type_id i32)
    (param $element_type_id i32) (param $element_size i32)
    (param $elements_are_references i32)
    (result i32)
    (local $array i32)
    (local $shape i32)
    (local $elements i32)
    (local $dimension i32)
    (local $length i32)
    (local $total i32)
    (local $stride i32)
    local.get $rank
    i32.eqz
    if unreachable end
    local.get $dimensions
    i32.eqz
    if unreachable end
    i32.const 1
    local.set $total
    local.get $rank
    local.set $dimension
    block $validated
      loop $validate
        local.get $dimension
        i32.eqz
        br_if $validated
        local.get $dimension
        i32.const 1
        i32.sub
        local.tee $dimension
        i32.const 2
        i32.shl
        local.get $dimensions
        i32.add
        i32.load
        local.tee $length
        i32.const 0
        i32.lt_s
        if unreachable end
        local.get $length
        i32.const 1073741823
        i32.gt_u
        if i32.const 0 return end
        local.get $length
        if
          local.get $total
          i32.const 1073741823
          local.get $length
          i32.div_u
          i32.gt_u
          if i32.const 0 return end
        end
        local.get $total
        local.get $length
        i32.mul
        local.set $total
        br $validate
      end
    end
    local.get $rank
    i32.const 8
    i32.mul
    i32.const 0
    call $allocate
    local.set $shape
    local.get $total
    if
      local.get $total
      local.get $elements_are_references
      if (result i32)
        i32.const 4
      else
        local.get $element_size
      end
      i32.mul
      i32.const 0
      call $allocate
      local.set $elements
    end
    i32.const 24
    local.get $type_id
    call $allocate
    local.set $array
    i32.const 1
    local.set $stride
    local.get $rank
    local.set $dimension
    block $shaped
      loop $shape_dimensions
        local.get $dimension
        i32.eqz
        br_if $shaped
        local.get $dimension
        i32.const 1
        i32.sub
        local.tee $dimension
        i32.const 2
        i32.shl
        local.get $dimensions
        i32.add
        i32.load
        local.set $length
        local.get $shape
        local.get $dimension
        i32.const 3
        i32.shl
        i32.add
        local.get $length
        i32.store
        local.get $shape
        local.get $dimension
        i32.const 3
        i32.shl
        i32.add
        local.get $stride
        i32.store offset=4
        local.get $stride
        local.get $length
        i32.mul
        local.set $stride
        br $shape_dimensions
      end
    end
    local.get $array
    local.get $total
    i32.store offset=4
    local.get $array
    local.get $elements
    i32.store offset=8
    local.get $array
    local.get $element_type_id
    i32.store offset=12
    local.get $array
    local.get $rank
    i32.store offset=16
    local.get $array
    local.get $shape
    i32.store offset=20
    local.get $array)

  (func (export "array_rank") (param $array i32) (result i32)
    (local $type_id i32)
    local.get $array
    i32.eqz
    if unreachable end
    local.get $array
    i32.load
    local.set $type_id
    global.get $type_sizes
    local.get $type_id
    i32.const 2
    i32.shl
    i32.add
    i32.load
    i32.const 24
    i32.eq
    if (result i32)
      local.get $array
      i32.load offset=16
    else
      i32.const 1
    end)

  (func (export "array_get_length")
    (param $array i32) (param $dimension i32) (result i32)
    (local $type_id i32)
    (local $rank i32)
    local.get $array
    i32.eqz
    if unreachable end
    local.get $array
    i32.load
    local.set $type_id
    global.get $type_sizes
    local.get $type_id
    i32.const 2
    i32.shl
    i32.add
    i32.load
    i32.const 24
    i32.ne
    if
      local.get $dimension
      i32.eqz
      if (result i32)
        local.get $array
        i32.load offset=4
      else
        i32.const -1
      end
      return
    end
    local.get $array
    i32.load offset=16
    local.tee $rank
    local.get $dimension
    i32.le_u
    local.get $dimension
    i32.const 0
    i32.lt_s
    i32.or
    if
      i32.const -1
      return
    end
    local.get $array
    i32.load offset=20
    local.get $dimension
    i32.const 3
    i32.shl
    i32.add
    i32.load)

  (func (export "allocate_string")
    (param $value i32) (param $length i32) (param $type_id i32) (result i32)
    (local $string i32)
    (local $index i32)
    local.get $length
    i32.const 0
    i32.lt_s
    if
      unreachable
    end
    local.get $length
    i32.const 2147483642
    i32.gt_u
    if
      i32.const 0
      return
    end
    local.get $length
    i32.const 2
    i32.mul
    i32.const 8
    i32.add
    local.get $type_id
    call $allocate
    local.tee $string
    local.get $length
    i32.store offset=4
    block $done
      loop $fill
        local.get $index
        local.get $length
        i32.ge_u
        br_if $done
        local.get $string
        local.get $index
        i32.const 2
        i32.mul
        i32.add
        local.get $value
        i32.store16 offset=8
        local.get $index
        i32.const 1
        i32.add
        local.set $index
        br $fill
      end
    end
    local.get $string)

  (func $native_aligned_alloc (export "native_aligned_alloc")
    (param $size i32) (param $alignment i32) (result i32)
    (local $allocation i32)
    (local $aligned i32)
    local.get $alignment
    i32.eqz
    if
      i32.const 0
      return
    end
    local.get $alignment
    local.get $alignment
    i32.const 1
    i32.sub
    i32.and
    i32.eqz
    i32.eqz
    if
      i32.const 0
      return
    end
    local.get $alignment
    i32.const 4
    i32.lt_u
    if
      i32.const 4
      local.set $alignment
    end
    local.get $size
    i32.eqz
    if
      i32.const 1
      local.set $size
    end
    local.get $size
    local.get $alignment
    i32.add
    i32.const 7
    i32.add
    local.tee $allocation
    local.get $size
    i32.lt_u
    if
      i32.const 0
      return
    end
    local.get $allocation
    call $reserve
    local.set $allocation
    local.get $allocation
    i32.const 8
    i32.add
    local.get $alignment
    i32.const 1
    i32.sub
    i32.add
    i32.const 0
    local.get $alignment
    i32.sub
    i32.and
    local.tee $aligned
    i32.const 8
    i32.sub
    local.get $size
    i32.store
    local.get $aligned
    i32.const 4
    i32.sub
    local.get $allocation
    i32.store
    local.get $aligned)

  (func (export "native_alloc") (param $size i32) (result i32)
    local.get $size
    i32.const 4
    call $native_aligned_alloc)

  (func $native_aligned_realloc (export "native_aligned_realloc")
    (param $address i32) (param $size i32) (param $alignment i32) (result i32)
    (local $replacement i32)
    (local $copy_size i32)
    local.get $size
    local.get $alignment
    call $native_aligned_alloc
    local.tee $replacement
    i32.eqz
    if
      i32.const 0
      return
    end
    local.get $address
    i32.eqz
    if
      local.get $replacement
      return
    end
    local.get $address
    i32.const 8
    i32.sub
    i32.load
    local.get $size
    i32.lt_u
    if (result i32)
      local.get $address
      i32.const 8
      i32.sub
      i32.load
    else
      local.get $size
    end
    local.set $copy_size
    local.get $replacement
    local.get $address
    local.get $copy_size
    memory.copy
    local.get $replacement)

  (func (export "native_realloc")
    (param $address i32) (param $size i32) (result i32)
    local.get $address
    local.get $size
    i32.const 4
    call $native_aligned_realloc)

  (func (export "native_free") (export "native_aligned_free")
    (param $address i32))

  ;; The integration gate proves post-link DCE removes this helper.
  (func $netwasm.unused_runtime_sentinel (result i32)
    i32.const 987654320))
