// Metreja Profiler — ARM64 Assembly Stubs for ELT3 Hooks
//
// ELT3WithInfo callbacks on ARM64 AAPCS64 receive:
//   X0 = FunctionIDOrClientID (UINT_PTR)
//   X1 = COR_PRF_ELT_INFO (UINT_PTR)
//
// These naked stubs save/restore ALL volatile registers to be transparent:
//   GPR volatile: X0-X17, LR (X30), FP (X29)
//   SIMD volatile: V0-V7, V16-V31
//   (V8-V15 are callee-saved per AAPCS64, no need to save)
//
// Stack layout (total 544 bytes, 16-byte aligned = 34 × 16):
//   [sp+0]   X29, X30 (frame pointer + link register)
//   [sp+16]  X0,  X1
//   [sp+32]  X2,  X3
//   [sp+48]  X4,  X5
//   [sp+64]  X6,  X7
//   [sp+80]  X8,  X9
//   [sp+96]  X10, X11
//   [sp+112] X12, X13
//   [sp+128] X14, X15
//   [sp+144] X16, X17
//   [sp+160] Q0,  Q1
//   [sp+192] Q2,  Q3
//   [sp+224] Q4,  Q5
//   [sp+256] Q6,  Q7
//   [sp+288] Q16, Q17
//   [sp+320] Q18, Q19
//   [sp+352] Q20, Q21
//   [sp+384] Q22, Q23
//   [sp+416] Q24, Q25
//   [sp+448] Q26, Q27
//   [sp+480] Q28, Q29
//   [sp+512] Q30, Q31

.text
.align 4

//-----------------------------------------------------------------------------
// Macro: Save all volatile registers (544 bytes)
//-----------------------------------------------------------------------------
.macro SAVE_REGS
    // Allocate stack frame (544 > 504, so can't use stp pre-index)
    sub     sp, sp, #544

    // Save frame pointer and link register
    stp     x29, x30, [sp]

    // Save volatile GPRs (X0-X17)
    stp     x0,  x1,  [sp, #16]
    stp     x2,  x3,  [sp, #32]
    stp     x4,  x5,  [sp, #48]
    stp     x6,  x7,  [sp, #64]
    stp     x8,  x9,  [sp, #80]
    stp     x10, x11, [sp, #96]
    stp     x12, x13, [sp, #112]
    stp     x14, x15, [sp, #128]
    stp     x16, x17, [sp, #144]

    // Save volatile SIMD registers: V0-V7 (8 regs = 4 pairs)
    stp     q0,  q1,  [sp, #160]
    stp     q2,  q3,  [sp, #192]
    stp     q4,  q5,  [sp, #224]
    stp     q6,  q7,  [sp, #256]

    // Save volatile SIMD registers: V16-V31 (16 regs = 8 pairs)
    stp     q16, q17, [sp, #288]
    stp     q18, q19, [sp, #320]
    stp     q20, q21, [sp, #352]
    stp     q22, q23, [sp, #384]
    stp     q24, q25, [sp, #416]
    stp     q26, q27, [sp, #448]
    stp     q28, q29, [sp, #480]
    stp     q30, q31, [sp, #512]
.endm

//-----------------------------------------------------------------------------
// Macro: Restore all volatile registers
//-----------------------------------------------------------------------------
.macro RESTORE_REGS
    // Restore SIMD V16-V31
    ldp     q30, q31, [sp, #512]
    ldp     q28, q29, [sp, #480]
    ldp     q26, q27, [sp, #448]
    ldp     q24, q25, [sp, #416]
    ldp     q22, q23, [sp, #384]
    ldp     q20, q21, [sp, #352]
    ldp     q18, q19, [sp, #320]
    ldp     q16, q17, [sp, #288]

    // Restore SIMD V0-V7
    ldp     q6,  q7,  [sp, #256]
    ldp     q4,  q5,  [sp, #224]
    ldp     q2,  q3,  [sp, #192]
    ldp     q0,  q1,  [sp, #160]

    // Restore GPRs X0-X17
    ldp     x16, x17, [sp, #144]
    ldp     x14, x15, [sp, #128]
    ldp     x12, x13, [sp, #112]
    ldp     x10, x11, [sp, #96]
    ldp     x8,  x9,  [sp, #80]
    ldp     x6,  x7,  [sp, #64]
    ldp     x4,  x5,  [sp, #48]
    ldp     x2,  x3,  [sp, #32]
    ldp     x0,  x1,  [sp, #16]

    // Restore frame pointer and link register
    ldp     x29, x30, [sp]

    // Deallocate stack frame
    add     sp, sp, #544
.endm

//-----------------------------------------------------------------------------
// EnterNaked — Called by CLR on method enter
//-----------------------------------------------------------------------------
.globl _EnterNaked
.align 4
_EnterNaked:
    SAVE_REGS

    // Load original X0, X1 from save area for the call
    ldp     x0, x1, [sp, #16]
    bl      _EnterStub

    RESTORE_REGS
    ret

//-----------------------------------------------------------------------------
// LeaveNaked — Called by CLR on method leave
//-----------------------------------------------------------------------------
.globl _LeaveNaked
.align 4
_LeaveNaked:
    SAVE_REGS

    ldp     x0, x1, [sp, #16]
    bl      _LeaveStub

    RESTORE_REGS
    ret

//-----------------------------------------------------------------------------
// TailcallNaked — Called by CLR on tail call
//-----------------------------------------------------------------------------
.globl _TailcallNaked
.align 4
_TailcallNaked:
    SAVE_REGS

    ldp     x0, x1, [sp, #16]
    bl      _TailcallStub

    RESTORE_REGS
    ret
