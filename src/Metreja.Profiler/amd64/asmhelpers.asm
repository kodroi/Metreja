; Metreja Profiler - x64 Assembly Stubs for ELT3 Hooks
;
; ELT3WithInfo callbacks receive:
;   RCX = FunctionIDOrClientID (UINT_PTR)
;   RDX = COR_PRF_ELT_INFO (UINT_PTR)
;
; These are "naked" stubs that must save/restore ALL volatile registers
; (RAX, RCX, RDX, R8, R9, R10, R11) plus XMM0-XMM5 before calling
; into C++ code, since the CLR expects these hooks to be transparent.
;
; Stack alignment:
;   On entry: RSP mod 16 == 0 (CLR guarantees alignment before call)
;   After return addr push by CALL: RSP mod 16 == 8
;   We push 7 GPRs (56 bytes): RSP mod 16 == 0  (8 + 56 = 64)
;   SUB RSP, 128: for 32 shadow + 6*16 XMM save = 128
;
; Layout after SUB RSP, 128:
;   [RSP+0]   to [RSP+31]  = shadow space (32 bytes)
;   [RSP+32]  = XMM0 save
;   [RSP+48]  = XMM1 save
;   [RSP+64]  = XMM2 save
;   [RSP+80]  = XMM3 save
;   [RSP+96]  = XMM4 save
;   [RSP+112] = XMM5 save

EXTERN EnterStub:PROC
EXTERN LeaveStub:PROC
EXTERN TailcallStub:PROC

;-----------------------------------------------------------------------------
; Macros to save/restore volatile registers
;-----------------------------------------------------------------------------

SAVE_REGS MACRO
    push    rax
    push    rcx
    push    rdx
    push    r8
    push    r9
    push    r10
    push    r11

    sub     rsp, 128

    movdqa  [rsp+32],  xmm0
    movdqa  [rsp+48],  xmm1
    movdqa  [rsp+64],  xmm2
    movdqa  [rsp+80],  xmm3
    movdqa  [rsp+96],  xmm4
    movdqa  [rsp+112], xmm5

    ; Reload original RCX/RDX from saved locations
    mov     rcx, [rsp+128+40]   ; original RCX
    mov     rdx, [rsp+128+32]   ; original RDX
ENDM

RESTORE_REGS MACRO
    movdqa  xmm0, [rsp+32]
    movdqa  xmm1, [rsp+48]
    movdqa  xmm2, [rsp+64]
    movdqa  xmm3, [rsp+80]
    movdqa  xmm4, [rsp+96]
    movdqa  xmm5, [rsp+112]

    add     rsp, 128

    pop     r11
    pop     r10
    pop     r9
    pop     r8
    pop     rdx
    pop     rcx
    pop     rax
ENDM

.code

;-----------------------------------------------------------------------------
; EnterNaked - Called by CLR on method enter
;-----------------------------------------------------------------------------
EnterNaked PROC
    SAVE_REGS
    call    EnterStub
    RESTORE_REGS
    ret
EnterNaked ENDP

;-----------------------------------------------------------------------------
; LeaveNaked - Called by CLR on method leave
;-----------------------------------------------------------------------------
LeaveNaked PROC
    SAVE_REGS
    call    LeaveStub
    RESTORE_REGS
    ret
LeaveNaked ENDP

;-----------------------------------------------------------------------------
; TailcallNaked - Called by CLR on tail call
;-----------------------------------------------------------------------------
TailcallNaked PROC
    SAVE_REGS
    call    TailcallStub
    RESTORE_REGS
    ret
TailcallNaked ENDP

END
