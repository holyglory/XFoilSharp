      INTEGER FUNCTION TRACE_LENTRIM(STR)
      CHARACTER*(*) STR
      INTEGER I
C
      TRACE_LENTRIM = LEN(STR)
      DO 10 I=LEN(STR),1,-1
        IF(STR(I:I).NE.' ') THEN
          TRACE_LENTRIM = I
          RETURN
        ENDIF
   10 CONTINUE
      TRACE_LENTRIM = 0
      RETURN
      END


      INTEGER FUNCTION TRACE_NEXTSEQ()
      INTEGER TRACESEQ
      SAVE TRACESEQ
      DATA TRACESEQ /0/
C
      TRACESEQ = TRACESEQ + 1
      TRACE_NEXTSEQ = TRACESEQ
      RETURN
      END


      SUBROUTINE TRACE_CLEAN(INPUT, OUTPUT)
      CHARACTER*(*) INPUT, OUTPUT
      INTEGER I, J, LIMIT, CODE
      CHARACTER*1 CH
C
      OUTPUT = ' '
      LIMIT = LEN(OUTPUT)
      J = 1
      DO 20 I=1, LEN(INPUT)
        IF(J.GT.LIMIT) GO TO 30
        CH = INPUT(I:I)
        CODE = ICHAR(CH)
        IF(CH.EQ.'"' .OR. CH.EQ.'\') THEN
          OUTPUT(J:J) = ' '
        ELSE IF(CODE.LT.32) THEN
          OUTPUT(J:J) = ' '
        ELSE
          OUTPUT(J:J) = CH
        ENDIF
        J = J + 1
   20 CONTINUE
   30 CONTINUE
      RETURN
      END


      SUBROUTINE TRACE_REALHEX(VALUE, HEX)
      REAL VALUE, VALUELOCAL
      INTEGER*4 BITS
      CHARACTER*(*) HEX
      EQUIVALENCE (VALUELOCAL, BITS)
C
      VALUELOCAL = VALUE
      WRITE(HEX,1000) BITS
 1000 FORMAT(Z8.8)
      RETURN
      END


      SUBROUTINE TRACE_OPEN(FILENAME)
      CHARACTER*(*) FILENAME
      INTEGER TRACE_LENTRIM, LPATH
      CHARACTER*1024 OVERRIDE
      LOGICAL LOPEN
C
      OVERRIDE = ' '
      CALL GETENV('XFOIL_TRACE_PIPE_PATH', OVERRIDE)
      LPATH = TRACE_LENTRIM(OVERRIDE)
      INQUIRE(UNIT=51, OPENED=LOPEN)
      IF(LOPEN) CLOSE(51)
      IF(LPATH.GT.0) THEN
        OPEN(51, FILE=OVERRIDE(1:LPATH), STATUS='OLD')
      ELSE
        OPEN(51, FILE=FILENAME, STATUS='REPLACE')
      ENDIF
      CALL TRACE_TEXT('session', 'session_start', 'fortran trace')
      RETURN
      END


      SUBROUTINE TRACE_CLOSE
      LOGICAL LOPEN
C
      INQUIRE(UNIT=51, OPENED=LOPEN)
      IF(.NOT.LOPEN) RETURN
      CALL TRACE_TEXT('session', 'session_end', ' ')
      CLOSE(51)
      RETURN
      END


      SUBROUTINE TRACE_ENTER(SCOPE)
      CHARACTER*(*) SCOPE
C
      RETURN
      END


      SUBROUTINE TRACE_EXIT(SCOPE)
      CHARACTER*(*) SCOPE
C
      RETURN
      END


      SUBROUTINE TRACE_TEXT(SCOPE, KIND, MESSAGE)
      CHARACTER*(*) SCOPE, KIND, MESSAGE
      INTEGER TRACE_LENTRIM, TRACE_NEXTSEQ
      INTEGER LK, LS, LM, SEQ
      LOGICAL LOPEN
      CHARACTER*128 CKIND, CSCOPE
      CHARACTER*1024 CMESSAGE
C
      INQUIRE(UNIT=51, OPENED=LOPEN)
      IF(.NOT.LOPEN) RETURN
C
      CALL TRACE_CLEAN(KIND, CKIND)
      CALL TRACE_CLEAN(SCOPE, CSCOPE)
      CALL TRACE_CLEAN(MESSAGE, CMESSAGE)
C
      LK = TRACE_LENTRIM(CKIND)
      LS = TRACE_LENTRIM(CSCOPE)
      LM = TRACE_LENTRIM(CMESSAGE)
      IF(LK.LE.0) LK = 1
      IF(LS.LE.0) LS = 1
      IF(LM.LE.0) THEN
        CMESSAGE(1:1) = ' '
        LM = 1
      ENDIF
C
      SEQ = TRACE_NEXTSEQ()
      WRITE(51,1000) SEQ, CKIND(1:LK), CSCOPE(1:LS),
     &               CMESSAGE(1:LM)
 1000 FORMAT('{"sequence":',I12,',"runtime":"fortran","kind":"',A,
     & '","scope":"',A,'","name":null,"data":{"message":"',A,
     & '"},"values":null,"tags":null,"timestampUtc":null}')
      RETURN
      END


      SUBROUTINE TRACE_ARRAY2(SCOPE, NAME, V1, V2)
      CHARACTER*(*) SCOPE, NAME
      INTEGER TRACE_LENTRIM, TRACE_NEXTSEQ
      INTEGER LS, LN, SEQ
      REAL V1, V2
      LOGICAL LOPEN
      CHARACTER*128 CSCOPE, CNAME
C
      INQUIRE(UNIT=51, OPENED=LOPEN)
      IF(.NOT.LOPEN) RETURN
C
      CALL TRACE_CLEAN(SCOPE, CSCOPE)
      CALL TRACE_CLEAN(NAME, CNAME)
      LS = TRACE_LENTRIM(CSCOPE)
      LN = TRACE_LENTRIM(CNAME)
      IF(LS.LE.0) LS = 1
      IF(LN.LE.0) LN = 1
C
      SEQ = TRACE_NEXTSEQ()
      WRITE(51,1000) SEQ, CSCOPE(1:LS), CNAME(1:LN), V1, V2
 1000 FORMAT('{"sequence":',I12,',"runtime":"fortran","kind":"array",'
     & '"scope":"',A,'","name":"',A,'","data":null,"values":[',
     & 1PE24.16,',',1PE24.16,
     & '],"tags":null,"timestampUtc":null}')
      RETURN
      END


      SUBROUTINE TRACE_ARRAY3(SCOPE, NAME, V1, V2, V3)
      CHARACTER*(*) SCOPE, NAME
      INTEGER TRACE_LENTRIM, TRACE_NEXTSEQ
      INTEGER LS, LN, SEQ
      REAL V1, V2, V3
      LOGICAL LOPEN
      CHARACTER*128 CSCOPE, CNAME
C
      INQUIRE(UNIT=51, OPENED=LOPEN)
      IF(.NOT.LOPEN) RETURN
C
      CALL TRACE_CLEAN(SCOPE, CSCOPE)
      CALL TRACE_CLEAN(NAME, CNAME)
      LS = TRACE_LENTRIM(CSCOPE)
      LN = TRACE_LENTRIM(CNAME)
      IF(LS.LE.0) LS = 1
      IF(LN.LE.0) LN = 1
C
      SEQ = TRACE_NEXTSEQ()
      WRITE(51,1000) SEQ, CSCOPE(1:LS), CNAME(1:LN), V1, V2, V3
 1000 FORMAT('{"sequence":',I12,',"runtime":"fortran","kind":"array",'
     & '"scope":"',A,'","name":"',A,'","data":null,"values":[',
     & 1PE24.16,',',1PE24.16,',',1PE24.16,
     & '],"tags":null,"timestampUtc":null}')
      RETURN
      END


      SUBROUTINE TRACE_ARRAY3_INDEX(SCOPE, NAME, IV, V1, V2, V3)
      CHARACTER*(*) SCOPE, NAME
      INTEGER TRACE_LENTRIM, TRACE_NEXTSEQ
      INTEGER LS, LN, SEQ, IV
      REAL V1, V2, V3
      LOGICAL LOPEN
      CHARACTER*128 CSCOPE, CNAME
C
      INQUIRE(UNIT=51, OPENED=LOPEN)
      IF(.NOT.LOPEN) RETURN
C
      CALL TRACE_CLEAN(SCOPE, CSCOPE)
      CALL TRACE_CLEAN(NAME, CNAME)
      LS = TRACE_LENTRIM(CSCOPE)
      LN = TRACE_LENTRIM(CNAME)
      IF(LS.LE.0) LS = 1
      IF(LN.LE.0) LN = 1
C
      SEQ = TRACE_NEXTSEQ()
      WRITE(51,1000) SEQ, CSCOPE(1:LS), CNAME(1:LN), IV, V1, V2, V3
 1000 FORMAT('{"sequence":',I12,',"runtime":"fortran","kind":"array",'
     & '"scope":"',A,'","name":"',A,'","data":{"iv":',I8,
     & '},"values":[',1PE24.16,',',1PE24.16,',',1PE24.16,
     & '],"tags":null,"timestampUtc":null}')
      RETURN
      END


      SUBROUTINE TRACE_COLUMN_ENTRY(SCOPE, KIND, SOURCEINDEX,
     &                             ROW, VALUE)
      CHARACTER*(*) SCOPE, KIND
      INTEGER TRACE_LENTRIM, TRACE_NEXTSEQ
      INTEGER LS, LK, SEQ, SOURCEINDEX, ROW
      REAL VALUE
      LOGICAL LOPEN
      CHARACTER*128 CSCOPE, CKIND
C
      INQUIRE(UNIT=51, OPENED=LOPEN)
      IF(.NOT.LOPEN) RETURN
C
      CALL TRACE_CLEAN(SCOPE, CSCOPE)
      CALL TRACE_CLEAN(KIND, CKIND)
      LS = TRACE_LENTRIM(CSCOPE)
      LK = TRACE_LENTRIM(CKIND)
      IF(LS.LE.0) LS = 1
      IF(LK.LE.0) LK = 1
C
      SEQ = TRACE_NEXTSEQ()
      WRITE(51,1000) SEQ, CKIND(1:LK), CSCOPE(1:LS),
     &               SOURCEINDEX, ROW, VALUE
 1000 FORMAT('{"sequence":',I12,',"runtime":"fortran","kind":"',A,
     & '","scope":"',A,'","name":null,"data":{"sourceIndex":',I8,
     & ',"row":',I8,',"value":',1PE24.16,
     & ',"precisionMode":"legacy"},"values":null,"tags":null,'
     & '"timestampUtc":null}')
      RETURN
      END


      SUBROUTINE TRACE_CONFIG(SCOPE, NITER)
      CHARACTER*(*) SCOPE
      INTEGER TRACE_LENTRIM, TRACE_NEXTSEQ
      INTEGER LS, SEQ, NITER
      LOGICAL LOPEN
      CHARACTER*128 CSCOPE
C
      INQUIRE(UNIT=51, OPENED=LOPEN)
      IF(.NOT.LOPEN) RETURN
      CALL TRACE_CLEAN(SCOPE, CSCOPE)
      LS = TRACE_LENTRIM(CSCOPE)
      IF(LS.LE.0) LS = 1
C
      SEQ = TRACE_NEXTSEQ()
      WRITE(51,1000) SEQ, CSCOPE(1:LS), NITER
 1000 FORMAT('{"sequence":',I12,',"runtime":"fortran","kind":"config",'
     & '"scope":"',A,'","name":null,"data":{"niter":',I8,
     & '},"values":null,"tags":null,"timestampUtc":null}')
      RETURN
      END


      SUBROUTINE TRACE_ITERATION_START(SCOPE, ITER)
      CHARACTER*(*) SCOPE
      INTEGER TRACE_LENTRIM, TRACE_NEXTSEQ
      INTEGER LS, SEQ, ITER
      LOGICAL LOPEN
      CHARACTER*128 CSCOPE
C
      INQUIRE(UNIT=51, OPENED=LOPEN)
      IF(.NOT.LOPEN) RETURN
      CALL TRACE_CLEAN(SCOPE, CSCOPE)
      LS = TRACE_LENTRIM(CSCOPE)
      IF(LS.LE.0) LS = 1
C
      SEQ = TRACE_NEXTSEQ()
      WRITE(51,1000) SEQ, CSCOPE(1:LS), ITER
 1000 FORMAT('{"sequence":',I12,',"runtime":"fortran",'
     & '"kind":"iteration_start","scope":"',A,
     & '","name":null,"data":{"iteration":',I8,
     & '},"values":null,"tags":null,"timestampUtc":null}')
      RETURN
      END


      SUBROUTINE TRACE_POST_UPDATE(SCOPE, ITER, RMSBL, RMXBL, RLX)
      CHARACTER*(*) SCOPE
      INTEGER TRACE_LENTRIM, TRACE_NEXTSEQ
      INTEGER LS, SEQ, ITER
      REAL RMSBL, RMXBL, RLX
      LOGICAL LOPEN
      CHARACTER*128 CSCOPE
C
      INQUIRE(UNIT=51, OPENED=LOPEN)
      IF(.NOT.LOPEN) RETURN
      CALL TRACE_CLEAN(SCOPE, CSCOPE)
      LS = TRACE_LENTRIM(CSCOPE)
      IF(LS.LE.0) LS = 1
C
      SEQ = TRACE_NEXTSEQ()
      WRITE(51,1000) SEQ, CSCOPE(1:LS), ITER, RMSBL, RMXBL, RLX
 1000 FORMAT('{"sequence":',I12,',"runtime":"fortran",'
     & '"kind":"post_update","scope":"',A,
     & '","name":null,"data":{"iteration":',I8,
     & ',"rmsbl":',1PE24.16,',"rmxbl":',1PE24.16,
     & ',"rlx":',1PE24.16,
     & '},"values":null,"tags":null,"timestampUtc":null}')
      RETURN
      END


      SUBROUTINE TRACE_POST_CALC(SCOPE, ITER, CL, CD, CM, CDF)
      CHARACTER*(*) SCOPE
      INTEGER TRACE_LENTRIM, TRACE_NEXTSEQ
      INTEGER LS, SEQ, ITER
      REAL CL, CD, CM, CDF
      LOGICAL LOPEN
      CHARACTER*128 CSCOPE
C
      INQUIRE(UNIT=51, OPENED=LOPEN)
      IF(.NOT.LOPEN) RETURN
      CALL TRACE_CLEAN(SCOPE, CSCOPE)
      LS = TRACE_LENTRIM(CSCOPE)
      IF(LS.LE.0) LS = 1
C
      SEQ = TRACE_NEXTSEQ()
      WRITE(51,1000) SEQ, CSCOPE(1:LS), ITER, CL, CD, CM, CDF
 1000 FORMAT('{"sequence":',I12,',"runtime":"fortran",'
     & '"kind":"post_calc","scope":"',A,
     & '","name":null,"data":{"iteration":',I8,
     & ',"cl":',1PE24.16,',"cd":',1PE24.16,
     & ',"cm":',1PE24.16,',"cdf":',1PE24.16,
     & '},"values":null,"tags":null,"timestampUtc":null}')
      RETURN
      END


      SUBROUTINE TRACE_CONVERGED(SCOPE, ITER)
      CHARACTER*(*) SCOPE
      INTEGER TRACE_LENTRIM, TRACE_NEXTSEQ
      INTEGER LS, SEQ, ITER
      LOGICAL LOPEN
      CHARACTER*128 CSCOPE
C
      INQUIRE(UNIT=51, OPENED=LOPEN)
      IF(.NOT.LOPEN) RETURN
      CALL TRACE_CLEAN(SCOPE, CSCOPE)
      LS = TRACE_LENTRIM(CSCOPE)
      IF(LS.LE.0) LS = 1
C
      SEQ = TRACE_NEXTSEQ()
      WRITE(51,1000) SEQ, CSCOPE(1:LS), ITER
 1000 FORMAT('{"sequence":',I12,',"runtime":"fortran",'
     & '"kind":"converged","scope":"',A,
     & '","name":null,"data":{"iteration":',I8,
     & '},"values":null,"tags":null,"timestampUtc":null}')
      RETURN
      END


      SUBROUTINE TRACE_RELAXATION_FACTOR(SCOPE, RLX, DAC, CLNEW)
      CHARACTER*(*) SCOPE
      INTEGER TRACE_LENTRIM, TRACE_NEXTSEQ
      INTEGER LS, SEQ
      REAL RLX, DAC, CLNEW
      LOGICAL LOPEN
      CHARACTER*128 CSCOPE
C
      INQUIRE(UNIT=51, OPENED=LOPEN)
      IF(.NOT.LOPEN) RETURN
      CALL TRACE_CLEAN(SCOPE, CSCOPE)
      LS = TRACE_LENTRIM(CSCOPE)
      IF(LS.LE.0) LS = 1
C
      SEQ = TRACE_NEXTSEQ()
      WRITE(51,1000) SEQ, CSCOPE(1:LS), RLX, DAC, CLNEW
 1000 FORMAT('{"sequence":',I12,',"runtime":"fortran",'
     & '"kind":"relaxation_factor","scope":"',A,
     & '","name":null,"data":{"rlx":',1PE24.16,
     & ',"dac":',1PE24.16,',"clnew":',1PE24.16,
     & '},"values":null,"tags":null,"timestampUtc":null}')
      RETURN
      END


      SUBROUTINE TRACE_STATION_STATE(SCOPE, IS, IBL, IV,
     &                               XSI, UEI, THI, DSI, MDI,
     &                               DUE2, DDS2)
      CHARACTER*(*) SCOPE
      INTEGER TRACE_LENTRIM, TRACE_NEXTSEQ
      INTEGER LS, SEQ, IS, IBL, IV
      REAL XSI, UEI, THI, DSI, MDI, DUE2, DDS2
      LOGICAL LOPEN
      CHARACTER*128 CSCOPE
C
      INQUIRE(UNIT=51, OPENED=LOPEN)
      IF(.NOT.LOPEN) RETURN
      CALL TRACE_CLEAN(SCOPE, CSCOPE)
      LS = TRACE_LENTRIM(CSCOPE)
      IF(LS.LE.0) LS = 1
C
      SEQ = TRACE_NEXTSEQ()
      WRITE(51,1000) SEQ, CSCOPE(1:LS), IS, IBL, IV,
     &               XSI, UEI, THI, DSI, MDI, DUE2, DDS2
 1000 FORMAT('{"sequence":',I12,',"runtime":"fortran",'
     & '"kind":"station_state","scope":"',A,
     & '","name":null,"data":{"side":',I4,
     & ',"station":',I6,',"iv":',I6,
     & ',"xsi":',1PE24.16,',"uei":',1PE24.16,
     & ',"thi":',1PE24.16,',"dsi":',1PE24.16,
     & ',"mdi":',1PE24.16,',"due2":',1PE24.16,
     & ',"dds2":',1PE24.16,
     & '},"values":null,"tags":null,"timestampUtc":null}')
      RETURN
      END


      SUBROUTINE TRACE_STATION_UPDATE(SCOPE, IS, IBL, IV,
     &                                DUEDG, CTAU, THET, DSTR, MASS)
      CHARACTER*(*) SCOPE
      INTEGER TRACE_LENTRIM, TRACE_NEXTSEQ
      INTEGER LS, SEQ, IS, IBL, IV
      REAL DUEDG, CTAU, THET, DSTR, MASS
      LOGICAL LOPEN
      CHARACTER*128 CSCOPE
C
      INQUIRE(UNIT=51, OPENED=LOPEN)
      IF(.NOT.LOPEN) RETURN
      CALL TRACE_CLEAN(SCOPE, CSCOPE)
      LS = TRACE_LENTRIM(CSCOPE)
      IF(LS.LE.0) LS = 1
C
      SEQ = TRACE_NEXTSEQ()
      WRITE(51,1000) SEQ, CSCOPE(1:LS), IS, IBL, IV,
     &               DUEDG, CTAU, THET, DSTR, MASS
 1000 FORMAT('{"sequence":',I12,',"runtime":"fortran",'
     & '"kind":"station_update","scope":"',A,
     & '","name":null,"data":{"side":',I4,
     & ',"station":',I6,',"iv":',I6,
     & ',"duedg":',1PE24.16,',"ctau":',1PE24.16,
     & ',"theta":',1PE24.16,',"dstar":',1PE24.16,
     & ',"mass":',1PE24.16,
     & '},"values":null,"tags":null,"timestampUtc":null}')
      RETURN
      END


      SUBROUTINE TRACE_SETBL_FORCING_INPUTS(SCOPE, IS, IBL, IV,
     &    UEDGSTATIONVAL, USAVSTATIONVAL, D2U2VAL, DUE2VAL, DDS2VAL,
     &    UEDGLE1VAL, USAVLE1VAL, DULE1VAL,
     &    UEDGLE2VAL, USAVLE2VAL, DULE2VAL,
     &    SSTGOVAL, SSTGPVAL, XIULE1VAL, XIULE2VAL, XIFORCINGVAL)
      CHARACTER*(*) SCOPE
      INTEGER TRACE_LENTRIM, TRACE_NEXTSEQ
      INTEGER LS, SEQ, IS, IBL, IV
      REAL UEDGSTATIONVAL, USAVSTATIONVAL, D2U2VAL, DUE2VAL, DDS2VAL
      REAL UEDGLE1VAL, USAVLE1VAL, DULE1VAL
      REAL UEDGLE2VAL, USAVLE2VAL, DULE2VAL
      REAL SSTGOVAL, SSTGPVAL, XIULE1VAL, XIULE2VAL, XIFORCINGVAL
      LOGICAL LOPEN
      CHARACTER*128 CSCOPE
C
      INQUIRE(UNIT=51, OPENED=LOPEN)
      IF(.NOT.LOPEN) RETURN
      CALL TRACE_CLEAN(SCOPE, CSCOPE)
      LS = TRACE_LENTRIM(CSCOPE)
      IF(LS.LE.0) LS = 1
C
      SEQ = TRACE_NEXTSEQ()
      WRITE(51,1000) SEQ, CSCOPE(1:LS), IS, IBL, IV,
     &               UEDGSTATIONVAL, USAVSTATIONVAL, D2U2VAL,
     &               DUE2VAL, DDS2VAL,
     &               UEDGLE1VAL, USAVLE1VAL, DULE1VAL,
     &               UEDGLE2VAL, USAVLE2VAL, DULE2VAL,
     &               SSTGOVAL, SSTGPVAL, XIULE1VAL, XIULE2VAL,
     &               XIFORCINGVAL
 1000 FORMAT('{"sequence":',I12,',"runtime":"fortran",'
     & '"kind":"setbl_forcing_inputs","scope":"',A,
     & '","name":null,"data":{"side":',I4,
     & ',"station":',I6,',"iv":',I6,
     & ',"uedgStation":',1PE24.16,
     & ',"usavStation":',1PE24.16,',"d2_u2":',1PE24.16,
     & ',"due2":',1PE24.16,',"dds2":',1PE24.16,
     & ',"uedgLe1":',1PE24.16,',"usavLe1":',1PE24.16,
     & ',"dule1":',1PE24.16,',"uedgLe2":',1PE24.16,
     & ',"usavLe2":',1PE24.16,',"dule2":',1PE24.16,
     & ',"sstGo":',1PE24.16,',"sstGp":',1PE24.16,
     & ',"xiUle1":',1PE24.16,',"xiUle2":',1PE24.16,
     & ',"xiForcing":',1PE24.16,
     & '},"values":null,"tags":null,"timestampUtc":null}')
      RETURN
      END


      SUBROUTINE TRACE_SETBL_VDEL_TERMS(SCOPE, IS, IBL, IV, ROW,
     &    RESIDUALVAL,
     &    VS1UVAL, DUE1VAL, VS1DUETERMVAL,
     &    VS1DVAL, DDS1VAL, VS1DDSTERMVAL,
     &    VS2UVAL, DUE2VAL, VS2DUETERMVAL,
     &    VS2DVAL, DDS2VAL, VS2DDSTERMVAL,
     &    VS1XVAL, VS2XVAL, VSXVAL,
     &    XIFORCINGVAL, XIBASETERMVAL, XIVSXTERMVAL,
     &    FINALVAL)
      CHARACTER*(*) SCOPE
      INTEGER TRACE_LENTRIM, TRACE_NEXTSEQ
      INTEGER LS, SEQ, IS, IBL, IV, ROW
      REAL RESIDUALVAL
      REAL VS1UVAL, DUE1VAL, VS1DUETERMVAL
      REAL VS1DVAL, DDS1VAL, VS1DDSTERMVAL
      REAL VS2UVAL, DUE2VAL, VS2DUETERMVAL
      REAL VS2DVAL, DDS2VAL, VS2DDSTERMVAL
      REAL VS1XVAL, VS2XVAL, VSXVAL
      REAL XIFORCINGVAL, XIBASETERMVAL, XIVSXTERMVAL
      REAL FINALVAL
      LOGICAL LOPEN
      CHARACTER*128 CSCOPE
C
      INQUIRE(UNIT=51, OPENED=LOPEN)
      IF(.NOT.LOPEN) RETURN
      CALL TRACE_CLEAN(SCOPE, CSCOPE)
      LS = TRACE_LENTRIM(CSCOPE)
      IF(LS.LE.0) LS = 1
C
      SEQ = TRACE_NEXTSEQ()
      WRITE(51,1000) SEQ, CSCOPE(1:LS), IS, IBL, IV, ROW,
     &               RESIDUALVAL,
     &               VS1UVAL, DUE1VAL, VS1DUETERMVAL,
     &               VS1DVAL, DDS1VAL, VS1DDSTERMVAL,
     &               VS2UVAL, DUE2VAL, VS2DUETERMVAL,
     &               VS2DVAL, DDS2VAL, VS2DDSTERMVAL,
     &               VS1XVAL, VS2XVAL, VSXVAL,
     &               XIFORCINGVAL, XIBASETERMVAL, XIVSXTERMVAL,
     &               FINALVAL
 1000 FORMAT('{"sequence":',I12,',"runtime":"fortran",'
     & '"kind":"setbl_vdel_terms","scope":"',A,
     & '","name":null,"data":{"side":',I4,
     & ',"station":',I6,',"iv":',I6,',"row":',I4,
     & ',"residual":',1PE24.16,
     & ',"vs1U":',1PE24.16,',"due1":',1PE24.16,
     & ',"vs1DueTerm":',1PE24.16,
     & ',"vs1D":',1PE24.16,',"dds1":',1PE24.16,
     & ',"vs1DdsTerm":',1PE24.16,
     & ',"vs2U":',1PE24.16,',"due2":',1PE24.16,
     & ',"vs2DueTerm":',1PE24.16,
     & ',"vs2D":',1PE24.16,',"dds2":',1PE24.16,
     & ',"vs2DdsTerm":',1PE24.16,
     & ',"vs1X":',1PE24.16,',"vs2X":',1PE24.16,
     & ',"vsx":',1PE24.16,',"xiForcing":',1PE24.16,
     & ',"xiBaseTerm":',1PE24.16,',"xiVsxTerm":',1PE24.16,
     & ',"final":',1PE24.16,
     & '},"values":null,"tags":null,"timestampUtc":null}')
      RETURN
      END


      SUBROUTINE TRACE_LEGACY_SEED_SYSTEM(SCOPE, IS, IBL, ITER,
     & MODE, VS2, VSREZ)
      CHARACTER*(*) SCOPE, MODE
      INTEGER TRACE_LENTRIM, TRACE_NEXTSEQ
      INTEGER LS, LM, SEQ, IS, IBL, ITER
      REAL VS2(4,4), VSREZ(4)
      LOGICAL LOPEN
      CHARACTER*128 CSCOPE
      CHARACTER*32 CMODE
C
      INQUIRE(UNIT=51, OPENED=LOPEN)
      IF(.NOT.LOPEN) RETURN
      CALL TRACE_CLEAN(SCOPE, CSCOPE)
      CALL TRACE_CLEAN(MODE, CMODE)
      LS = TRACE_LENTRIM(CSCOPE)
      LM = TRACE_LENTRIM(CMODE)
      IF(LS.LE.0) LS = 1
      IF(LM.LE.0) LM = 1
C
      SEQ = TRACE_NEXTSEQ()
      WRITE(51,1000) SEQ, CSCOPE(1:LS), IS, IBL, ITER, CMODE(1:LM),
     &               VS2(1,1), VS2(1,2), VS2(1,3), VS2(1,4),
     &               VS2(2,1), VS2(2,2), VS2(2,3), VS2(2,4),
     &               VS2(3,1), VS2(3,2), VS2(3,3), VS2(3,4),
     &               VS2(4,1), VS2(4,2), VS2(4,3), VS2(4,4),
     &               VSREZ(1), VSREZ(2), VSREZ(3), VSREZ(4)
 1000 FORMAT('{"sequence":',I12,',"runtime":"fortran",'
     & '"kind":"legacy_seed_final_system","scope":"',A,
     & '","name":null,"data":{"side":',I4,
     & ',"station":',I6,',"iteration":',I6,
     & ',"mode":"',A,'","row11":',1PE24.16,
     & ',"row12":',1PE24.16,',"row13":',1PE24.16,
     & ',"row14":',1PE24.16,',"row21":',1PE24.16,
     & ',"row22":',1PE24.16,',"row23":',1PE24.16,
     & ',"row24":',1PE24.16,',"row31":',1PE24.16,
     & ',"row32":',1PE24.16,',"row33":',1PE24.16,
     & ',"row34":',1PE24.16,',"row41":',1PE24.16,
     & ',"row42":',1PE24.16,',"row43":',1PE24.16,
     & ',"row44":',1PE24.16,',"rhs1":',1PE24.16,
     & ',"rhs2":',1PE24.16,',"rhs3":',1PE24.16,
     & ',"rhs4":',1PE24.16,
     & '},"values":null,"tags":null,"timestampUtc":null}')
      RETURN
      END


      SUBROUTINE TRACE_LEGACY_SEED_DELTA(SCOPE, IS, IBL, ITER,
     & MODE, VSREZ)
      CHARACTER*(*) SCOPE, MODE
      INTEGER TRACE_LENTRIM, TRACE_NEXTSEQ
      INTEGER LS, LM, SEQ, IS, IBL, ITER
      REAL VSREZ(4)
      LOGICAL LOPEN
      CHARACTER*128 CSCOPE
      CHARACTER*32 CMODE
C
      INQUIRE(UNIT=51, OPENED=LOPEN)
      IF(.NOT.LOPEN) RETURN
      CALL TRACE_CLEAN(SCOPE, CSCOPE)
      CALL TRACE_CLEAN(MODE, CMODE)
      LS = TRACE_LENTRIM(CSCOPE)
      LM = TRACE_LENTRIM(CMODE)
      IF(LS.LE.0) LS = 1
      IF(LM.LE.0) LM = 1
C
      SEQ = TRACE_NEXTSEQ()
      WRITE(51,1000) SEQ, CSCOPE(1:LS), IS, IBL, ITER, CMODE(1:LM),
     &               VSREZ(1), VSREZ(2), VSREZ(3), VSREZ(4)
 1000 FORMAT('{"sequence":',I12,',"runtime":"fortran",'
     & '"kind":"legacy_seed_final_delta","scope":"',A,
     & '","name":null,"data":{"side":',I4,
     & ',"station":',I6,',"iteration":',I6,
     & ',"mode":"',A,'","delta1":',1PE24.16,
     & ',"delta2":',1PE24.16,',"delta3":',1PE24.16,
     & ',"delta4":',1PE24.16,
     & '},"values":null,"tags":null,"timestampUtc":null}')
      RETURN
      END


      SUBROUTINE TRACE_LEGACY_SEED_CONSTRAINT(SCOPE, IS, IBL, ITER,
     & MODE, CURRENTU2, CURRENTU2UEI, HK2, HKREF, UEREF,
     & SENS, SENNEW)
      CHARACTER*(*) SCOPE, MODE
      INTEGER TRACE_LENTRIM, TRACE_NEXTSEQ
      INTEGER LS, LM, SEQ, IS, IBL, ITER
      REAL CURRENTU2, CURRENTU2UEI, HK2, HKREF, UEREF, SENS, SENNEW
      LOGICAL LOPEN
      CHARACTER*128 CSCOPE
      CHARACTER*32 CMODE
C
      INQUIRE(UNIT=51, OPENED=LOPEN)
      IF(.NOT.LOPEN) RETURN
      CALL TRACE_CLEAN(SCOPE, CSCOPE)
      CALL TRACE_CLEAN(MODE, CMODE)
      LS = TRACE_LENTRIM(CSCOPE)
      LM = TRACE_LENTRIM(CMODE)
      IF(LS.LE.0) LS = 1
      IF(LM.LE.0) LM = 1
C
      SEQ = TRACE_NEXTSEQ()
      WRITE(51,1000) SEQ, CSCOPE(1:LS), IS, IBL, ITER, CMODE(1:LM),
     &               CURRENTU2, CURRENTU2UEI, HK2, HKREF, UEREF,
     &               SENS, SENNEW
 1000 FORMAT('{"sequence":',I12,',"runtime":"fortran",'
     & '"kind":"legacy_seed_constraint","scope":"',A,
     & '","name":null,"data":{"side":',I4,
     & ',"station":',I6,',"iteration":',I6,
     & ',"mode":"',A,'","currentU2":',1PE24.16,
     & ',"currentU2Uei":',1PE24.16,',"hk2":',1PE24.16,
     & ',"hkref":',1PE24.16,',"ueref":',1PE24.16,
     & ',"sens":',1PE24.16,',"senNew":',1PE24.16,
     & '},"values":null,"tags":null,"timestampUtc":null}')
      RETURN
      END


      SUBROUTINE TRACE_LEGACY_SEED_ITERATION(SCOPE, IS, IBL, ITER,
     & WAKE, TURB, TRAN, DMAX, RLX, UEI, THI, DSI, CTAU,
     & AMPL, RESNORM)
      CHARACTER*(*) SCOPE
      INTEGER TRACE_LENTRIM, TRACE_NEXTSEQ
      INTEGER LS, SEQ, IS, IBL, ITER
      INTEGER IWAKE, ITURB, ITRAN
      REAL DMAX, RLX, UEI, THI, DSI, CTAU, AMPL, RESNORM
      LOGICAL WAKE, TURB, TRAN, LOPEN
      CHARACTER*128 CSCOPE
      CHARACTER*5 CWAKE, CTURB, CTRAN
C
      INQUIRE(UNIT=51, OPENED=LOPEN)
      IF(.NOT.LOPEN) RETURN
      CALL TRACE_CLEAN(SCOPE, CSCOPE)
      LS = TRACE_LENTRIM(CSCOPE)
      IF(LS.LE.0) LS = 1
      IWAKE = 0
      IF(WAKE) IWAKE = 1
      ITURB = 0
      IF(TURB) ITURB = 1
      ITRAN = 0
      IF(TRAN) ITRAN = 1
      CWAKE = 'false'
      IF(IWAKE.EQ.1) CWAKE = 'true '
      CTURB = 'false'
      IF(ITURB.EQ.1) CTURB = 'true '
      CTRAN = 'false'
      IF(ITRAN.EQ.1) CTRAN = 'true '
C
      SEQ = TRACE_NEXTSEQ()
      WRITE(51,1000) SEQ, CSCOPE(1:LS), IS, IBL, ITER,
     &               CWAKE, CTURB, CTRAN, DMAX, RLX, UEI, THI,
     &               DSI, CTAU, AMPL, RESNORM
 1000 FORMAT('{"sequence":',I12,',"runtime":"fortran",'
     & '"kind":"legacy_seed_iteration","scope":"',A,
     & '","name":null,"data":{"side":',I4,
     & ',"station":',I6,',"iteration":',I6,
     & ',"wake":',A,',"turb":',A,',"tran":',A,
     & ',"dmax":',1PE24.16,',"rlx":',1PE24.16,
     & ',"uei":',1PE24.16,',"theta":',1PE24.16,
     & ',"dstar":',1PE24.16,',"ctau":',1PE24.16,
     & ',"ampl":',1PE24.16,',"residualNorm":',1PE24.16,
     & '},"values":null,"tags":null,"timestampUtc":null}')
      RETURN
      END


      SUBROUTINE TRACE_LEGACY_SEED_FINAL(SCOPE, IS, IBL,
     & WAKE, TURB, TRAN, CONVERGED, UEI, THI, DSI, CTAU,
     & AMPL, ITRANSTATION, MASS)
      CHARACTER*(*) SCOPE
      INTEGER TRACE_LENTRIM, TRACE_NEXTSEQ
      INTEGER LS, SEQ, IS, IBL, ITRANSTATION
      INTEGER IWAKE, ITURB, ITRAN, ICONV
      REAL UEI, THI, DSI, CTAU, AMPL, MASS
      LOGICAL WAKE, TURB, TRAN, CONVERGED, LOPEN
      CHARACTER*128 CSCOPE
      CHARACTER*5 CWAKE, CTURB, CTRAN, CCONV
C
      INQUIRE(UNIT=51, OPENED=LOPEN)
      IF(.NOT.LOPEN) RETURN
      CALL TRACE_CLEAN(SCOPE, CSCOPE)
      LS = TRACE_LENTRIM(CSCOPE)
      IF(LS.LE.0) LS = 1
      IWAKE = 0
      IF(WAKE) IWAKE = 1
      ITURB = 0
      IF(TURB) ITURB = 1
      ITRAN = 0
      IF(TRAN) ITRAN = 1
      ICONV = 0
      IF(CONVERGED) ICONV = 1
      CWAKE = 'false'
      IF(IWAKE.EQ.1) CWAKE = 'true '
      CTURB = 'false'
      IF(ITURB.EQ.1) CTURB = 'true '
      CTRAN = 'false'
      IF(ITRAN.EQ.1) CTRAN = 'true '
      CCONV = 'false'
      IF(ICONV.EQ.1) CCONV = 'true '
C
      SEQ = TRACE_NEXTSEQ()
      WRITE(51,1000) SEQ, CSCOPE(1:LS), IS, IBL,
     &               CWAKE, CTURB, CTRAN, CCONV,
     &               UEI, THI, DSI, CTAU, AMPL, ITRANSTATION, MASS
 1000 FORMAT('{"sequence":',I12,',"runtime":"fortran",'
     & '"kind":"legacy_seed_final","scope":"',A,
     & '","name":null,"data":{"side":',I4,
     & ',"station":',I6,',"wake":',A,
     & ',"turb":',A,',"tran":',A,',"converged":',A,
     & ',"uei":',1PE24.16,',"theta":',1PE24.16,
     & ',"dstar":',1PE24.16,',"ctau":',1PE24.16,
     & ',"ampl":',1PE24.16,',"transitionStation":',I6,
     & ',"mass":',1PE24.16,
     & '},"values":null,"tags":null,"timestampUtc":null}')
      RETURN
      END


      SUBROUTINE TRACE_LAMINAR_SEED_STEP(SCOPE, IS, IBL, ITER, MODE,
     & UEI, THI, DSI, AMPL, DELSHR, DELTH, DELDS, DELUE,
     & RATSHR, RATTH, RATDS, RATUE, DMAX, RLX, RESNORM)
      CHARACTER*(*) SCOPE, MODE
      INTEGER TRACE_LENTRIM, TRACE_NEXTSEQ
      INTEGER LS, LM, SEQ, IS, IBL, ITER
      REAL UEI, THI, DSI, AMPL, DELSHR, DELTH, DELDS, DELUE,
     &     RATSHR, RATTH, RATDS, RATUE, DMAX, RLX, RESNORM
      LOGICAL LOPEN
      CHARACTER*128 CSCOPE, CMODE
C
      INQUIRE(UNIT=51, OPENED=LOPEN)
      IF(.NOT.LOPEN) RETURN
      CALL TRACE_CLEAN(SCOPE, CSCOPE)
      CALL TRACE_CLEAN(MODE, CMODE)
      LS = TRACE_LENTRIM(CSCOPE)
      LM = TRACE_LENTRIM(CMODE)
      IF(LS.LE.0) LS = 1
      IF(LM.LE.0) LM = 1
C
      SEQ = TRACE_NEXTSEQ()
      WRITE(51,1000) SEQ, CSCOPE(1:LS), IS, IBL, ITER,
     &               CMODE(1:LM), UEI, THI, DSI, AMPL,
     &               DELSHR, DELTH, DELDS, DELUE,
     &               RATSHR, RATTH, RATDS, RATUE,
     &               DMAX, RLX, RESNORM
 1000 FORMAT('{"sequence":',I12,',"runtime":"fortran",'
     & '"kind":"laminar_seed_step","scope":"',A,
     & '","name":null,"data":{"side":',I4,
     & ',"station":',I6,',"iteration":',I6,
     & ',"mode":"',A,'","uei":',1PE24.16,
     & ',"theta":',1PE24.16,',"dstar":',1PE24.16,
     & ',"ampl":',1PE24.16,
     & ',"deltaShear":',1PE24.16,',"deltaTheta":',1PE24.16,
     & ',"deltaDstar":',1PE24.16,',"deltaUe":',1PE24.16,
     & ',"ratioShear":',1PE24.16,',"ratioTheta":',1PE24.16,
     & ',"ratioDstar":',1PE24.16,',"ratioUe":',1PE24.16,
     & ',"dmax":',1PE24.16,
     & ',"rlx":',1PE24.16,',"residualNorm":',1PE24.16,
     & '},"values":null,"tags":null,"timestampUtc":null}')
      RETURN
      END


      SUBROUTINE TRACE_LAMINAR_SEED_STEP_NORM(SCOPE, IS, IBL, ITER,
     & MODE, DELSHR, DELTH, DELDS, SQSHR, SQTH, SQDS, SUMSQ,
     & RESNORM)
      CHARACTER*(*) SCOPE, MODE
      INTEGER TRACE_LENTRIM, TRACE_NEXTSEQ
      INTEGER LS, LM, SEQ, IS, IBL, ITER
      REAL DELSHR, DELTH, DELDS, SQSHR, SQTH, SQDS, SUMSQ, RESNORM
      LOGICAL LOPEN
      CHARACTER*128 CSCOPE, CMODE
C
      INQUIRE(UNIT=51, OPENED=LOPEN)
      IF(.NOT.LOPEN) RETURN
      CALL TRACE_CLEAN(SCOPE, CSCOPE)
      CALL TRACE_CLEAN(MODE, CMODE)
      LS = TRACE_LENTRIM(CSCOPE)
      LM = TRACE_LENTRIM(CMODE)
      IF(LS.LE.0) LS = 1
      IF(LM.LE.0) LM = 1
C
      SEQ = TRACE_NEXTSEQ()
      WRITE(51,1000) SEQ, CSCOPE(1:LS), IS, IBL, ITER,
     &               CMODE(1:LM), DELSHR, DELTH, DELDS,
     &               SQSHR, SQTH, SQDS, SUMSQ, RESNORM
 1000 FORMAT('{"sequence":',I12,',"runtime":"fortran",'
     & '"kind":"laminar_seed_step_norm_terms","scope":"',A,
     & '","name":null,"data":{"side":',I4,
     & ',"station":',I6,',"iteration":',I6,
     & ',"mode":"',A,'","deltaShear":',1PE24.16,
     & ',"deltaTheta":',1PE24.16,',"deltaDstar":',1PE24.16,
     & ',"squareShear":',1PE24.16,',"squareTheta":',1PE24.16,
     & ',"squareDstar":',1PE24.16,',"sumSquares":',1PE24.16,
     & ',"residualNorm":',1PE24.16,
     & '},"values":null,"tags":null,"timestampUtc":null}')
      RETURN
      END


      SUBROUTINE TRACE_GAUSS_STATE(SCOPE, PHASE, NP, KROW,
     & A11, A12, A13, A14, A21, A22, A23, A24,
     & A31, A32, A33, A34, A41, A42, A43, A44,
     & B1, B2, B3, B4)
      CHARACTER*(*) SCOPE, PHASE
      INTEGER TRACE_LENTRIM, TRACE_NEXTSEQ
      INTEGER LS, LP, SEQ, NP, KROW
      REAL A11, A12, A13, A14, A21, A22, A23, A24
      REAL A31, A32, A33, A34, A41, A42, A43, A44
      REAL B1, B2, B3, B4
      LOGICAL LOPEN
      CHARACTER*128 CSCOPE, CPHASE
C
      INQUIRE(UNIT=51, OPENED=LOPEN)
      IF(.NOT.LOPEN) RETURN
      CALL TRACE_CLEAN(SCOPE, CSCOPE)
      CALL TRACE_CLEAN(PHASE, CPHASE)
      LS = TRACE_LENTRIM(CSCOPE)
      LP = TRACE_LENTRIM(CPHASE)
      IF(LS.LE.0) LS = 1
      IF(LP.LE.0) LP = 1
C
      SEQ = TRACE_NEXTSEQ()
      WRITE(51,1000) SEQ, CSCOPE(1:LS), CPHASE(1:LP), NP, KROW,
     & A11, A12, A13, A14, A21, A22, A23, A24,
     & A31, A32, A33, A34, A41, A42, A43, A44,
     & B1, B2, B3, B4
 1000 FORMAT('{"sequence":',I12,',"runtime":"fortran",'
     & '"kind":"gauss_state","scope":"',A,
     & '","name":null,"data":{"phase":"',A,
     & '","pivotIndex":',I6,',"rowIndex":',I6,
     & ',"row11":',1PE24.16,',"row12":',1PE24.16,
     & ',"row13":',1PE24.16,',"row14":',1PE24.16,
     & ',"row21":',1PE24.16,',"row22":',1PE24.16,
     & ',"row23":',1PE24.16,',"row24":',1PE24.16,
     & ',"row31":',1PE24.16,',"row32":',1PE24.16,
     & ',"row33":',1PE24.16,',"row34":',1PE24.16,
     & ',"row41":',1PE24.16,',"row42":',1PE24.16,
     & ',"row43":',1PE24.16,',"row44":',1PE24.16,
     & ',"rhs1":',1PE24.16,',"rhs2":',1PE24.16,
     & ',"rhs3":',1PE24.16,',"rhs4":',1PE24.16,
     & '},"values":null,"tags":null,"timestampUtc":null}')
      RETURN
      END


      SUBROUTINE TRACE_LAMINAR_SEED_FINAL(SCOPE, IS, IBL, THI, DSI,
     &                                    AMPL, CTAU, MASS)
      CHARACTER*(*) SCOPE
      INTEGER TRACE_LENTRIM, TRACE_NEXTSEQ
      INTEGER LS, SEQ, IS, IBL
      REAL THI, DSI, AMPL, CTAU, MASS
      LOGICAL LOPEN
      CHARACTER*128 CSCOPE
C
      INQUIRE(UNIT=51, OPENED=LOPEN)
      IF(.NOT.LOPEN) RETURN
      CALL TRACE_CLEAN(SCOPE, CSCOPE)
      LS = TRACE_LENTRIM(CSCOPE)
      IF(LS.LE.0) LS = 1
C
      SEQ = TRACE_NEXTSEQ()
      WRITE(51,1000) SEQ, CSCOPE(1:LS), IS, IBL, THI, DSI, AMPL, CTAU,
     & MASS
 1000 FORMAT('{"sequence":',I12,',"runtime":"fortran",'
     & '"kind":"laminar_seed_final","scope":"',A,
     & '","name":null,"data":{"side":',I4,
     & ',"station":',I6,',"theta":',1PE24.16,
     & ',"dstar":',1PE24.16,',"ampl":',1PE24.16,
     & ',"ctau":',1PE24.16,',"mass":',1PE24.16,
     & '},"values":null,"tags":null,"timestampUtc":null}')
      RETURN
      END


      SUBROUTINE TRACE_LAMINAR_SEED_SYSTEM(SCOPE, IS, IBL, ITER,
     & MODE, UEI, THI, DSI, AMPL, CTAU, HK2, HK2T2, HK2D2, HK2U2,
     & HTARG, REZ1, REZ2, REZ3, REZ4,
     & R11, R12, R13, R14, R21, R22, R23, R24,
     & R31, R32, R33, R34, R41, R42, R43, R44)
      CHARACTER*(*) SCOPE, MODE
      INTEGER TRACE_LENTRIM, TRACE_NEXTSEQ
      INTEGER LS, LM, SEQ, IS, IBL, ITER
      REAL UEI, THI, DSI, AMPL, CTAU, HK2, HK2T2, HK2D2, HK2U2
      REAL HTARG, REZ1, REZ2, REZ3, REZ4
      REAL R11, R12, R13, R14, R21, R22, R23, R24
      REAL R31, R32, R33, R34, R41, R42, R43, R44
      LOGICAL LOPEN
      CHARACTER*128 CSCOPE, CMODE
C
      INQUIRE(UNIT=51, OPENED=LOPEN)
      IF(.NOT.LOPEN) RETURN
      CALL TRACE_CLEAN(SCOPE, CSCOPE)
      CALL TRACE_CLEAN(MODE, CMODE)
      LS = TRACE_LENTRIM(CSCOPE)
      LM = TRACE_LENTRIM(CMODE)
      IF(LS.LE.0) LS = 1
      IF(LM.LE.0) LM = 1
C
      SEQ = TRACE_NEXTSEQ()
      WRITE(51,1000) SEQ, CSCOPE(1:LS), IS, IBL, ITER, CMODE(1:LM),
     & UEI, THI, DSI, AMPL, CTAU, HK2, HK2T2, HK2D2, HK2U2, HTARG,
     & REZ1, REZ2, REZ3, REZ4,
     & R11, R12, R13, R14, R21, R22, R23, R24,
     & R31, R32, R33, R34, R41, R42, R43, R44
 1000 FORMAT('{"sequence":',I12,',"runtime":"fortran",'
     & '"kind":"laminar_seed_system","scope":"',A,
     & '","name":null,"data":{"side":',I4,
     & ',"station":',I6,',"iteration":',I6,
     & ',"mode":"',A,'","uei":',1PE24.16,
     & ',"theta":',1PE24.16,',"dstar":',1PE24.16,
     & ',"ampl":',1PE24.16,',"ctau":',1PE24.16,
     & ',"hk2":',1PE24.16,
     & ',"hk2_T2":',1PE24.16,',"hk2_D2":',1PE24.16,
     & ',"hk2_U2":',1PE24.16,',"htarg":',1PE24.16,
     & ',"residual1":',1PE24.16,',"residual2":',1PE24.16,
     & ',"residual3":',1PE24.16,',"residual4":',1PE24.16,
     & ',"row11":',1PE24.16,',"row12":',1PE24.16,
     & ',"row13":',1PE24.16,',"row14":',1PE24.16,
     & ',"row21":',1PE24.16,',"row22":',1PE24.16,
     & ',"row23":',1PE24.16,',"row24":',1PE24.16,
     & ',"row31":',1PE24.16,',"row32":',1PE24.16,
     & ',"row33":',1PE24.16,',"row34":',1PE24.16,
     & ',"row41":',1PE24.16,',"row42":',1PE24.16,
     & ',"row43":',1PE24.16,',"row44":',1PE24.16,
     & '},"values":null,"tags":null,"timestampUtc":null}')
      RETURN
      END


      SUBROUTINE TRACE_BLDIF_LAMINAR_AX_TERMS(SCOPE,
     &    X1, X2, T1, T2, D1, D2, HK1, HK2, RT1, RT2, AMPL1, AMPL2,
     &    ZAX, AX, AXHK1, AXT1, AXRT1, AXA1, AXHK2, AXT2, AXRT2, AXA2,
     &    VS1T1, VS1T2, VS1T3, VS1INNER, VS1D,
     &    VS2T1, VS2T2, VS2T3, VS2INNER, VS2D)
      CHARACTER*(*) SCOPE
      INTEGER TRACE_LENTRIM, TRACE_NEXTSEQ
      INTEGER LS, SEQ
      REAL X1, X2, T1, T2, D1, D2, HK1, HK2, RT1, RT2, AMPL1, AMPL2
      REAL ZAX, AX, AXHK1, AXT1, AXRT1, AXA1, AXHK2, AXT2, AXRT2, AXA2
      REAL VS1T1, VS1T2, VS1T3, VS1INNER, VS1D
      REAL VS2T1, VS2T2, VS2T3, VS2INNER, VS2D
      LOGICAL LOPEN
      CHARACTER*128 CSCOPE
C
      INQUIRE(UNIT=51, OPENED=LOPEN)
      IF(.NOT.LOPEN) RETURN
      CALL TRACE_CLEAN(SCOPE, CSCOPE)
      LS = TRACE_LENTRIM(CSCOPE)
      IF(LS.LE.0) LS = 1
C
      SEQ = TRACE_NEXTSEQ()
      WRITE(51,1000) SEQ, CSCOPE(1:LS),
     &     X1, X2, T1, T2, D1, D2, HK1, HK2, RT1, RT2, AMPL1, AMPL2,
     &     ZAX, AX, AXHK1, AXT1, AXRT1, AXA1, AXHK2, AXT2, AXRT2, AXA2,
     &     VS1T1, VS1T2, VS1T3, VS1INNER, VS1D,
     &     VS2T1, VS2T2, VS2T3, VS2INNER, VS2D
 1000 FORMAT('{"sequence":',I12,',"runtime":"fortran",'
     & '"kind":"laminar_ax_terms","scope":"',A,
     & '","name":null,"data":{"x1":',1PE24.16,
     & ',"x2":',1PE24.16,',"t1":',1PE24.16,
     & ',"t2":',1PE24.16,',"d1":',1PE24.16,
     & ',"d2":',1PE24.16,',"hk1":',1PE24.16,
     & ',"hk2":',1PE24.16,',"rt1":',1PE24.16,
     & ',"rt2":',1PE24.16,',"ampl1":',1PE24.16,
     & ',"ampl2":',1PE24.16,',"zAx":',1PE24.16,
     & ',"ax":',1PE24.16,',"axHk1":',1PE24.16,
     & ',"axT1":',1PE24.16,',"axRt1":',1PE24.16,
     & ',"axA1":',1PE24.16,',"axHk2":',1PE24.16,
     & ',"axT2":',1PE24.16,',"axRt2":',1PE24.16,
     & ',"axA2":',1PE24.16,',"vs1Row12Term1":',1PE24.16,
     & ',"vs1Row12Term2":',1PE24.16,
     & ',"vs1Row12Term3":',1PE24.16,
     & ',"vs1Row12Inner":',1PE24.16,
     & ',"vs1Row13Inner":',1PE24.16,
     & ',"vs2Row12Term1":',1PE24.16,
     & ',"vs2Row12Term2":',1PE24.16,
     & ',"vs2Row12Term3":',1PE24.16,
     & ',"vs2Row12Inner":',1PE24.16,
     & ',"vs2Row13Inner":',1PE24.16,
     & '},"values":null,"tags":null,"timestampUtc":null}')
      RETURN
      END


      SUBROUTINE TRACE_SIMI_PRECOMBINE_ROWS(SCOPE, SIDE, STATION,
     & EQ2VS1_22, EQ2VS2_22, EQ2COMB22,
     & EQ2VS1_24, EQ2VS2_24, EQ2COMB24,
     & EQ3VS1_32, EQ3VS2_32, EQ3COMB32,
     & EQ3VS1_33, EQ3VS2_33, EQ3COMB33)
      CHARACTER*(*) SCOPE
      INTEGER TRACE_LENTRIM, TRACE_NEXTSEQ
      INTEGER LS, SEQ, SIDE, STATION
      REAL EQ2VS1_22, EQ2VS2_22, EQ2COMB22
      REAL EQ2VS1_24, EQ2VS2_24, EQ2COMB24
      REAL EQ3VS1_32, EQ3VS2_32, EQ3COMB32
      REAL EQ3VS1_33, EQ3VS2_33, EQ3COMB33
      LOGICAL LOPEN
      CHARACTER*128 CSCOPE
C
      INQUIRE(UNIT=51, OPENED=LOPEN)
      IF(.NOT.LOPEN) RETURN
      CALL TRACE_CLEAN(SCOPE, CSCOPE)
      LS = TRACE_LENTRIM(CSCOPE)
      IF(LS.LE.0) LS = 1
C
      SEQ = TRACE_NEXTSEQ()
      WRITE(51,1000) SEQ, CSCOPE(1:LS), SIDE, STATION,
     &               EQ2VS1_22, EQ2VS2_22, EQ2COMB22,
     &               EQ2VS1_24, EQ2VS2_24, EQ2COMB24,
     &               EQ3VS1_32, EQ3VS2_32, EQ3COMB32,
     &               EQ3VS1_33, EQ3VS2_33, EQ3COMB33
 1000 FORMAT('{"sequence":',I12,',"runtime":"fortran",'
     & '"kind":"simi_precombine_rows","scope":"',A,
     & '","name":null,"data":{"side":',I8,',"station":',I8,
     & ',"eq2Vs1_22":',1PE24.16,
     & ',"eq2Vs2_22":',1PE24.16,',"eq2Combined22":',1PE24.16,
     & ',"eq2Vs1_24":',1PE24.16,
     & ',"eq2Vs2_24":',1PE24.16,',"eq2Combined24":',1PE24.16,
     & ',"eq3Vs1_32":',1PE24.16,',"eq3Vs2_32":',1PE24.16,
     & ',"eq3Combined32":',1PE24.16,
     & ',"eq3Vs1_33":',1PE24.16,',"eq3Vs2_33":',1PE24.16,
     & ',"eq3Combined33":',1PE24.16,
     & '},"values":null,"tags":null,"timestampUtc":null}')
      RETURN
      END


      SUBROUTINE TRACE_BLVAR_OUTER_DI_TERMS(SCOPE, STATION, ITYP,
     & HS_T, US_T, RT_T,
     & DD, DD_HS, DD_US, DD_S, DD_T,
     & DDL, DDL_HS, DDL_US, DDL_RT, DDL_T,
     & FINAL_DI_T)
      CHARACTER*(*) SCOPE
      INTEGER STATION, ITYP
      REAL HS_T, US_T, RT_T
      REAL DD, DD_HS, DD_US, DD_S, DD_T
      REAL DDL, DDL_HS, DDL_US, DDL_RT, DDL_T
      REAL FINAL_DI_T
      INTEGER TRACE_LENTRIM, TRACE_NEXTSEQ
      INTEGER LS, SEQ
      LOGICAL LOPEN
      CHARACTER*128 CSCOPE
C
      INQUIRE(UNIT=51, OPENED=LOPEN)
      IF(.NOT.LOPEN) RETURN
      CALL TRACE_CLEAN(SCOPE, CSCOPE)
      LS = TRACE_LENTRIM(CSCOPE)
      IF(LS.LE.0) LS = 1
C
      SEQ = TRACE_NEXTSEQ()
      WRITE(51,1000) SEQ, CSCOPE(1:LS),
     &     STATION, ITYP,
     &     HS_T, US_T, RT_T,
     &     DD, DD_HS, DD_US, DD_S, DD_T,
     &     DDL, DDL_HS, DDL_US, DDL_RT, DDL_T,
     &     FINAL_DI_T
 1000 FORMAT('{"sequence":',I12,',"runtime":"fortran",'
     & '"kind":"blvar_outer_di_terms","scope":"',A,
     & '","name":null,"data":{"station":',I0,',"ityp":',I0,
     & ',"hsT":',1PE24.16,',"usT":',1PE24.16,',"rtT":',1PE24.16,
     & ',"dd":',1PE24.16,',"ddHs":',1PE24.16,',"ddUs":',1PE24.16,
     & ',"ddS":',1PE24.16,',"ddT":',1PE24.16,
     & ',"ddl":',1PE24.16,',"ddlHs":',1PE24.16,
     & ',"ddlUs":',1PE24.16,',"ddlRt":',1PE24.16,
     & ',"ddlT":',1PE24.16,',"finalDiT":',1PE24.16,
     & '},"values":null,"tags":null,"timestampUtc":null}')
      RETURN
      END


      SUBROUTINE TRACE_BLDIF_EQ1_RESIDUAL_TERMS(SCOPE, SIDE, STATION,
     & PHASE, ITYP,
     & X1VAL, X2VAL,
     & SCCVAL, CQAVAL, UPWVAL, OMPWVAL, S1VAL, S2VAL,
     & SALEFTVAL, SARIGHTVAL, CQ1VAL, CQ2VAL,
     & LEFTVAL, RIGHTVAL, SAVAL, ALDVAL, DXIVAL, DEAVAL,
     & SLOGVAL, UQVAL, ULOGVAL,
     & SRCVAL, PRODVAL, LOGLOSSVAL, CONVVAL, DUXGAINVAL,
     & SUB1VAL, REZ1VAL, SUB2VAL, REZ2VAL, SUB3VAL, REZ3VAL,
     & REZCVAL)
      CHARACTER*(*) SCOPE
      INTEGER TRACE_LENTRIM, TRACE_NEXTSEQ
      INTEGER LS, SEQ, SIDE, STATION, PHASE, ITYP
      REAL X1VAL, X2VAL
      REAL SCCVAL, CQAVAL, UPWVAL, OMPWVAL, CQ1VAL, CQ2VAL
      REAL S1VAL, S2VAL, SALEFTVAL, SARIGHTVAL
      REAL LEFTVAL, RIGHTVAL, SAVAL, ALDVAL, DXIVAL, DEAVAL
      REAL SLOGVAL, UQVAL, ULOGVAL
      REAL SRCVAL, PRODVAL, LOGLOSSVAL, CONVVAL, DUXGAINVAL
      REAL SUB1VAL, REZ1VAL, SUB2VAL, REZ2VAL, SUB3VAL, REZ3VAL
      REAL REZCVAL
      LOGICAL LOPEN
      CHARACTER*128 CSCOPE
C
      INQUIRE(UNIT=51, OPENED=LOPEN)
      IF(.NOT.LOPEN) RETURN
      CALL TRACE_CLEAN(SCOPE, CSCOPE)
      LS = TRACE_LENTRIM(CSCOPE)
      IF(LS.LE.0) LS = 1
C
      SEQ = TRACE_NEXTSEQ()
      WRITE(51,1000) SEQ, CSCOPE(1:LS), SIDE, STATION, PHASE, ITYP,
     &               X1VAL, X2VAL,
     &               SCCVAL, CQAVAL, UPWVAL, OMPWVAL,
     &               S1VAL, S2VAL, SALEFTVAL, SARIGHTVAL,
     &               CQ1VAL, CQ2VAL, LEFTVAL, RIGHTVAL,
     &               SAVAL, ALDVAL,
     &               DXIVAL, DEAVAL, SLOGVAL, UQVAL, ULOGVAL,
     &               SRCVAL, PRODVAL, LOGLOSSVAL, CONVVAL,
     &               DUXGAINVAL,
     &               SUB1VAL, REZ1VAL, SUB2VAL, REZ2VAL,
     &               SUB3VAL, REZ3VAL, REZCVAL
 1000 FORMAT('{"sequence":',I12,',"runtime":"fortran",'
     & '"kind":"bldif_eq1_residual_terms","scope":"',A,
     & '","name":null,"data":{"side":',I2,
     & ',"station":',I4,
     & ',"phase":',I2,
     & ',"ityp":',I2,
     & ',"x1":',1PE24.16,
     & ',"x2":',1PE24.16,
     & ',"scc":',1PE24.16,',"cqa":',1PE24.16,
     & ',"upw":',1PE24.16,',"oneMinusUpw":',1PE24.16,
     & ',"s1":',1PE24.16,',"s2":',1PE24.16,
     & ',"saLeftTerm":',1PE24.16,
     & ',"saRightTerm":',1PE24.16,
     & ',"cq1":',1PE24.16,',"cq2":',1PE24.16,
     & ',"cqaLeftTerm":',1PE24.16,
     & ',"cqaRightTerm":',1PE24.16,
     & ',"sa":',1PE24.16,',"ald":',1PE24.16,
     & ',"dxi":',1PE24.16,',"dea":',1PE24.16,
     & ',"slog":',1PE24.16,',"uq":',1PE24.16,
     & ',"ulog":',1PE24.16,',"eq1Source":',1PE24.16,
     & ',"eq1Production":',1PE24.16,
     & ',"eq1LogLoss":',1PE24.16,
     & ',"eq1Convection":',1PE24.16,
     & ',"eq1DuxGain":',1PE24.16,
     & ',"eq1SubStored":',1PE24.16,
     & ',"rezcStoredTerms":',1PE24.16,
     & ',"eq1SubInlineProduction":',1PE24.16,
     & ',"rezcInlineProduction":',1PE24.16,
     & ',"eq1SubInlineFull":',1PE24.16,
     & ',"rezcInlineFull":',1PE24.16,
     & ',"rezc":',1PE24.16,
     & '},"values":null,"tags":null,"timestampUtc":null}')
      RETURN
      END


      SUBROUTINE TRACE_BLDIF_EQ1_X_TERMS(SCOPE, ITYP,
     & ZDXIBASETERM, ZDXIDUXTERM, ZDXI, ZX1, ZX2)
      CHARACTER*(*) SCOPE
      INTEGER TRACE_LENTRIM, TRACE_NEXTSEQ
      INTEGER LS, SEQ, ITYP
      REAL ZDXIBASETERM, ZDXIDUXTERM, ZDXI, ZX1, ZX2
      LOGICAL LOPEN
      CHARACTER*128 CSCOPE
C
      INQUIRE(UNIT=51, OPENED=LOPEN)
      IF(.NOT.LOPEN) RETURN
      CALL TRACE_CLEAN(SCOPE, CSCOPE)
      LS = TRACE_LENTRIM(CSCOPE)
      IF(LS.LE.0) LS = 1
C
      SEQ = TRACE_NEXTSEQ()
      WRITE(51,1000) SEQ, CSCOPE(1:LS), ITYP,
     & ZDXIBASETERM, ZDXIDUXTERM, ZDXI, ZX1, ZX2
 1000 FORMAT('{"sequence":',I12,',"runtime":"fortran",'
     & '"kind":"bldif_eq1_x_terms","scope":"',A,
     & '","name":null,"data":{"ityp":',I2,
     & ',"zDxiBaseTerm":',1PE24.16,
     & ',"zDxiDuxTerm":',1PE24.16,
     & ',"zDxi":',1PE24.16,
     & ',"zX1":',1PE24.16,
     & ',"zX2":',1PE24.16,
     & '},"values":null,"tags":null,"timestampUtc":null}')
      RETURN
      END


      SUBROUTINE TRACE_BLDIF_EQ2_X_BREAKDOWN(SCOPE, ITYP,
     & CFXX1, XLOGTERM1, CFXTERM1, ZX1,
     & CFXX2, XLOGTERM2, CFXTERM2, ZX2)
      CHARACTER*(*) SCOPE
      INTEGER TRACE_LENTRIM, TRACE_NEXTSEQ
      INTEGER LS, SEQ, ITYP
      REAL CFXX1, XLOGTERM1, CFXTERM1, ZX1
      REAL CFXX2, XLOGTERM2, CFXTERM2, ZX2
      LOGICAL LOPEN
      CHARACTER*128 CSCOPE
C
      INQUIRE(UNIT=51, OPENED=LOPEN)
      IF(.NOT.LOPEN) RETURN
      CALL TRACE_CLEAN(SCOPE, CSCOPE)
      LS = TRACE_LENTRIM(CSCOPE)
      IF(LS.LE.0) LS = 1
C
      SEQ = TRACE_NEXTSEQ()
      WRITE(51,1000) SEQ, CSCOPE(1:LS), ITYP,
     & CFXX1, XLOGTERM1, CFXTERM1, ZX1,
     & CFXX2, XLOGTERM2, CFXTERM2, ZX2
 1000 FORMAT('{"sequence":',I12,',"runtime":"fortran",'
     & '"kind":"bldif_eq2_x_breakdown","scope":"',A,
     & '","name":null,"data":{"ityp":',I2,
     & ',"cfxX1":',1PE24.16,
     & ',"xLogTerm":',1PE24.16,
     & ',"cfxTerm":',1PE24.16,
     & ',"zX1":',1PE24.16,
     & ',"cfxX2":',1PE24.16,
     & ',"x2LogTerm":',1PE24.16,
     & ',"cfx2Term":',1PE24.16,
     & ',"zX2":',1PE24.16,
     & '},"values":null,"tags":null,"timestampUtc":null}')
      RETURN
      END


      SUBROUTINE TRACE_BLDIF_EQ2_X_TERMS(SCOPE, ITYP,
     & ZXL, ZCFX, ZX1, ZX2)
      CHARACTER*(*) SCOPE
      INTEGER TRACE_LENTRIM, TRACE_NEXTSEQ
      INTEGER LS, SEQ, ITYP
      REAL ZXL, ZCFX, ZX1, ZX2
      LOGICAL LOPEN
      CHARACTER*128 CSCOPE
C
      INQUIRE(UNIT=51, OPENED=LOPEN)
      IF(.NOT.LOPEN) RETURN
      CALL TRACE_CLEAN(SCOPE, CSCOPE)
      LS = TRACE_LENTRIM(CSCOPE)
      IF(LS.LE.0) LS = 1
C
      SEQ = TRACE_NEXTSEQ()
      WRITE(51,1000) SEQ, CSCOPE(1:LS), ITYP,
     & ZXL, ZCFX, ZX1, ZX2
 1000 FORMAT('{"sequence":',I12,',"runtime":"fortran",'
     & '"kind":"bldif_eq2_x_terms","scope":"',A,
     & '","name":null,"data":{"ityp":',I2,
     & ',"zXl":',1PE24.16,
     & ',"zCfx":',1PE24.16,
     & ',"zX1":',1PE24.16,
     & ',"zX2":',1PE24.16,
     & '},"values":null,"tags":null,"timestampUtc":null}')
      RETURN
      END


      SUBROUTINE TRACE_BLDIF_EQ1_UQ_TERMS(SCOPE, ITYP,
     & CFA, HKA, RTA, DA, ALD, HKC, HKCHKA, HKCRTA,
     & HR, HRHKA, HRRTA, UQ, UQHKA, UQRTA, UQCFA, UQDA)
      CHARACTER*(*) SCOPE
      INTEGER TRACE_LENTRIM, TRACE_NEXTSEQ
      INTEGER LS, SEQ, ITYP
      REAL CFA, HKA, RTA, DA, ALD, HKC, HKCHKA, HKCRTA
      REAL HR, HRHKA, HRRTA, UQ, UQHKA, UQRTA, UQCFA, UQDA
      LOGICAL LOPEN
      CHARACTER*128 CSCOPE
C
      INQUIRE(UNIT=51, OPENED=LOPEN)
      IF(.NOT.LOPEN) RETURN
      CALL TRACE_CLEAN(SCOPE, CSCOPE)
      LS = TRACE_LENTRIM(CSCOPE)
      IF(LS.LE.0) LS = 1
C
      SEQ = TRACE_NEXTSEQ()
      WRITE(51,1000) SEQ, CSCOPE(1:LS), ITYP,
     & CFA, HKA, RTA, DA, ALD, HKC, HKCHKA, HKCRTA,
     & HR, HRHKA, HRRTA, UQ, UQHKA, UQRTA, UQCFA, UQDA
 1000 FORMAT('{"sequence":',I12,',"runtime":"fortran",'
     & '"kind":"bldif_eq1_uq_terms","scope":"',A,
     & '","name":null,"data":{"ityp":',I2,
     & ',"cfa":',1PE24.16,',"hka":',1PE24.16,
     & ',"rta":',1PE24.16,',"da":',1PE24.16,
     & ',"ald":',1PE24.16,',"hkc":',1PE24.16,
     & ',"hkcHka":',1PE24.16,',"hkcRta":',1PE24.16,
     & ',"hr":',1PE24.16,',"hrHka":',1PE24.16,
     & ',"hrRta":',1PE24.16,',"uq":',1PE24.16,
     & ',"uqHka":',1PE24.16,',"uqRta":',1PE24.16,
     & ',"uqCfa":',1PE24.16,',"uqDa":',1PE24.16,
     & '},"values":null,"tags":null,"timestampUtc":null}')
      RETURN
      END


      SUBROUTINE TRACE_BLDIF_EQ1_D_TERMS(SCOPE, ITYP,
     & ZD1, ZUPW, UPWD1, ZDE1, DE1D1, ZUS1, US1D1,
     & ZCQ1, CQ1D1, ZCF1, CF1D1, ZHK1, HK1D1,
     & ROW13BASETERM, ROW13UPWTERM, ROW13DETERM, ROW13USTERM,
     & ROW13TRANSPORT, ROW13CQTERM, ROW13CFTERM, ROW13HKTERM, ROW13,
     & ZD2, UPWD2, ZDE2, DE2D2, ZUS2, US2D2,
     & ZCQ2, CQ2D2, ZCF2, CF2D2, ZHK2, HK2D2,
     & ROW23BASETERM, ROW23UPWTERM, ROW23DETERM, ROW23USTERM,
     & ROW23TRANSPORT, ROW23CQTERM, ROW23CFTERM, ROW23HKTERM, ROW23)
      CHARACTER*(*) SCOPE
      INTEGER TRACE_LENTRIM, TRACE_NEXTSEQ
      INTEGER LS, SEQ, ITYP
      REAL ZD1, ZUPW, UPWD1, ZDE1, DE1D1, ZUS1, US1D1
      REAL ZCQ1, CQ1D1, ZCF1, CF1D1, ZHK1, HK1D1
      REAL ROW13BASETERM, ROW13UPWTERM, ROW13DETERM, ROW13USTERM
      REAL ROW13TRANSPORT, ROW13CQTERM, ROW13CFTERM, ROW13HKTERM, ROW13
      REAL ZD2, UPWD2, ZDE2, DE2D2, ZUS2, US2D2
      REAL ZCQ2, CQ2D2, ZCF2, CF2D2, ZHK2, HK2D2
      REAL ROW23BASETERM, ROW23UPWTERM, ROW23DETERM, ROW23USTERM
      REAL ROW23TRANSPORT, ROW23CQTERM, ROW23CFTERM, ROW23HKTERM, ROW23
      LOGICAL LOPEN
      CHARACTER*128 CSCOPE
C
      INQUIRE(UNIT=51, OPENED=LOPEN)
      IF(.NOT.LOPEN) RETURN
      CALL TRACE_CLEAN(SCOPE, CSCOPE)
      LS = TRACE_LENTRIM(CSCOPE)
      IF(LS.LE.0) LS = 1
C
      SEQ = TRACE_NEXTSEQ()
      WRITE(51,1000) SEQ, CSCOPE(1:LS), ITYP,
     & ZD1, ZUPW, UPWD1, ZDE1, DE1D1, ZUS1, US1D1,
     & ZCQ1, CQ1D1, ZCF1, CF1D1, ZHK1, HK1D1,
     & ROW13BASETERM, ROW13UPWTERM, ROW13DETERM, ROW13USTERM,
     & ROW13TRANSPORT, ROW13CQTERM, ROW13CFTERM, ROW13HKTERM, ROW13,
     & ZD2, UPWD2, ZDE2, DE2D2, ZUS2, US2D2,
     & ZCQ2, CQ2D2, ZCF2, CF2D2, ZHK2, HK2D2,
     & ROW23BASETERM, ROW23UPWTERM, ROW23DETERM, ROW23USTERM,
     & ROW23TRANSPORT, ROW23CQTERM, ROW23CFTERM, ROW23HKTERM, ROW23
 1000 FORMAT('{"sequence":',I12,',"runtime":"fortran",'
     & '"kind":"bldif_eq1_d_terms","scope":"',A,
     & '","name":null,"data":{"ityp":',I2,
     & ',"zD1":',1PE24.16,',"zUpw":',1PE24.16,
     & ',"upwD1":',1PE24.16,',"zDe1":',1PE24.16,
     & ',"de1D1":',1PE24.16,',"zUs1":',1PE24.16,
     & ',"us1D1":',1PE24.16,',"zCq1":',1PE24.16,
     & ',"cq1D1":',1PE24.16,',"zCf1":',1PE24.16,
     & ',"cf1D1":',1PE24.16,',"zHk1":',1PE24.16,
     & ',"hk1D1":',1PE24.16,
     & ',"row13BaseTerm":',1PE24.16,
     & ',"row13UpwTerm":',1PE24.16,
     & ',"row13DeTerm":',1PE24.16,
     & ',"row13UsTerm":',1PE24.16,
     & ',"row13Transport":',1PE24.16,
     & ',"row13CqTerm":',1PE24.16,
     & ',"row13CfTerm":',1PE24.16,
     & ',"row13HkTerm":',1PE24.16,
     & ',"row13":',1PE24.16,
     & ',"zD2":',1PE24.16,',"upwD2":',1PE24.16,
     & ',"zDe2":',1PE24.16,',"de2D2":',1PE24.16,
     & ',"zUs2":',1PE24.16,',"us2D2":',1PE24.16,
     & ',"zCq2":',1PE24.16,',"cq2D2":',1PE24.16,
     & ',"zCf2":',1PE24.16,',"cf2D2":',1PE24.16,
     & ',"zHk2":',1PE24.16,',"hk2D2":',1PE24.16,
     & ',"row23BaseTerm":',1PE24.16,
     & ',"row23UpwTerm":',1PE24.16,
     & ',"row23DeTerm":',1PE24.16,
     & ',"row23UsTerm":',1PE24.16,
     & ',"row23Transport":',1PE24.16,
     & ',"row23CqTerm":',1PE24.16,
     & ',"row23CfTerm":',1PE24.16,
     & ',"row23HkTerm":',1PE24.16,
     & ',"row23":',1PE24.16,
     & '},"values":null,"tags":null,"timestampUtc":null}')
      RETURN
      END


      SUBROUTINE TRACE_BLDIF_EQ1_U_TERMS(SCOPE, ITYP,
     & ZU1, ZUPW, UPWU1, ZDE1, DE1U1, ZUS1, US1U1,
     & ZCQ1, CQ1U1, ZCF1, CF1U1, ZHK1, HK1U1,
     & ROW14BASETERM, ROW14UPWTERM, ROW14DETERM, ROW14USTERM,
     & ROW14TRANSPORT, ROW14CQTERM, ROW14CFTERM, ROW14HKTERM, ROW14,
     & ZU2, UPWU2, ZDE2, DE2U2, ZUS2, US2U2,
     & ZCQ2, CQ2U2, ZCF2, CF2U2, ZHK2, HK2U2,
     & ROW24BASETERM, ROW24UPWTERM, ROW24DETERM, ROW24USTERM,
     & ROW24TRANSPORT, ROW24CQTERM, ROW24CFTERM, ROW24HKTERM, ROW24)
      CHARACTER*(*) SCOPE
      INTEGER TRACE_LENTRIM, TRACE_NEXTSEQ
      INTEGER LS, SEQ, ITYP
      REAL ZU1, ZUPW, UPWU1, ZDE1, DE1U1, ZUS1, US1U1
      REAL ZCQ1, CQ1U1, ZCF1, CF1U1, ZHK1, HK1U1
      REAL ROW14BASETERM, ROW14UPWTERM, ROW14DETERM, ROW14USTERM
      REAL ROW14TRANSPORT, ROW14CQTERM, ROW14CFTERM, ROW14HKTERM, ROW14
      REAL ZU2, UPWU2, ZDE2, DE2U2, ZUS2, US2U2
      REAL ZCQ2, CQ2U2, ZCF2, CF2U2, ZHK2, HK2U2
      REAL ROW24BASETERM, ROW24UPWTERM, ROW24DETERM, ROW24USTERM
      REAL ROW24TRANSPORT, ROW24CQTERM, ROW24CFTERM, ROW24HKTERM, ROW24
      LOGICAL LOPEN
      CHARACTER*128 CSCOPE
C
      INQUIRE(UNIT=51, OPENED=LOPEN)
      IF(.NOT.LOPEN) RETURN
      CALL TRACE_CLEAN(SCOPE, CSCOPE)
      LS = TRACE_LENTRIM(CSCOPE)
      IF(LS.LE.0) LS = 1
C
      SEQ = TRACE_NEXTSEQ()
      WRITE(51,1000) SEQ, CSCOPE(1:LS), ITYP,
     & ZU1, ZUPW, UPWU1, ZDE1, DE1U1, ZUS1, US1U1,
     & ZCQ1, CQ1U1, ZCF1, CF1U1, ZHK1, HK1U1,
     & ROW14BASETERM, ROW14UPWTERM, ROW14DETERM, ROW14USTERM,
     & ROW14TRANSPORT, ROW14CQTERM, ROW14CFTERM, ROW14HKTERM, ROW14,
     & ZU2, UPWU2, ZDE2, DE2U2, ZUS2, US2U2,
     & ZCQ2, CQ2U2, ZCF2, CF2U2, ZHK2, HK2U2,
     & ROW24BASETERM, ROW24UPWTERM, ROW24DETERM, ROW24USTERM,
     & ROW24TRANSPORT, ROW24CQTERM, ROW24CFTERM, ROW24HKTERM, ROW24
 1000 FORMAT('{"sequence":',I12,',"runtime":"fortran",'
     & '"kind":"bldif_eq1_u_terms","scope":"',A,
     & '","name":null,"data":{"ityp":',I2,
     & ',"zU1":',1PE24.16,',"zUpw":',1PE24.16,
     & ',"upwU1":',1PE24.16,',"zDe1":',1PE24.16,
     & ',"de1U1":',1PE24.16,',"zUs1":',1PE24.16,
     & ',"us1U1":',1PE24.16,',"zCq1":',1PE24.16,
     & ',"cq1U1":',1PE24.16,',"zCf1":',1PE24.16,
     & ',"cf1U1":',1PE24.16,',"zHk1":',1PE24.16,
     & ',"hk1U1":',1PE24.16,
     & ',"row14BaseTerm":',1PE24.16,
     & ',"row14UpwTerm":',1PE24.16,
     & ',"row14DeTerm":',1PE24.16,
     & ',"row14UsTerm":',1PE24.16,
     & ',"row14Transport":',1PE24.16,
     & ',"row14CqTerm":',1PE24.16,
     & ',"row14CfTerm":',1PE24.16,
     & ',"row14HkTerm":',1PE24.16,
     & ',"row14":',1PE24.16,
     & ',"zU2":',1PE24.16,',"upwU2":',1PE24.16,
     & ',"zDe2":',1PE24.16,',"de2U2":',1PE24.16,
     & ',"zUs2":',1PE24.16,',"us2U2":',1PE24.16,
     & ',"zCq2":',1PE24.16,',"cq2U2":',1PE24.16,
     & ',"zCf2":',1PE24.16,',"cf2U2":',1PE24.16,
     & ',"zHk2":',1PE24.16,',"hk2U2":',1PE24.16,
     & ',"row24BaseTerm":',1PE24.16,
     & ',"row24UpwTerm":',1PE24.16,
     & ',"row24DeTerm":',1PE24.16,
     & ',"row24UsTerm":',1PE24.16,
     & ',"row24Transport":',1PE24.16,
     & ',"row24CqTerm":',1PE24.16,
     & ',"row24CfTerm":',1PE24.16,
     & ',"row24HkTerm":',1PE24.16,
     & ',"row24":',1PE24.16,
     & '},"values":null,"tags":null,"timestampUtc":null}')
      RETURN
      END


      SUBROUTINE TRACE_BLDIF_EQ1_T_TERMS(SCOPE, ITYP,
     & ZDE1, DE1T1, TERMUPW1, TERMDE1, TERMUS1, ROW12TRANSPORT,
     & ZCQ1, CQ1T1, TERMCQ1, ZCF1, CF1T1, TERMCF1,
     & ZHK1, HK1T1, TERMHK1, ROW12,
     & ZDE2, DE2T2, TERMUPW2, TERMDE2, TERMUS2, ROW22TRANSPORT,
     & ZCQ2, CQ2T2, TERMCQ2, ZCF2, CF2T2, TERMCF2,
     & ZHK2, HK2T2, TERMHK2, ROW22)
      CHARACTER*(*) SCOPE
      INTEGER TRACE_LENTRIM, TRACE_NEXTSEQ
      INTEGER LS, SEQ, ITYP
      REAL ZDE1, DE1T1, TERMUPW1, TERMDE1, TERMUS1, ROW12TRANSPORT
      REAL ZCQ1, CQ1T1, TERMCQ1, ZCF1, CF1T1, TERMCF1
      REAL ZHK1, HK1T1, TERMHK1, ROW12
      REAL ZDE2, DE2T2, TERMUPW2, TERMDE2, TERMUS2, ROW22TRANSPORT
      REAL ZCQ2, CQ2T2, TERMCQ2, ZCF2, CF2T2, TERMCF2
      REAL ZHK2, HK2T2, TERMHK2, ROW22
      LOGICAL LOPEN
      CHARACTER*128 CSCOPE
C
      INQUIRE(UNIT=51, OPENED=LOPEN)
      IF(.NOT.LOPEN) RETURN
      CALL TRACE_CLEAN(SCOPE, CSCOPE)
      LS = TRACE_LENTRIM(CSCOPE)
      IF(LS.LE.0) LS = 1
C
      SEQ = TRACE_NEXTSEQ()
      WRITE(51,1000) SEQ, CSCOPE(1:LS), ITYP,
     & ZDE1, DE1T1, TERMUPW1, TERMDE1, TERMUS1, ROW12TRANSPORT,
     & ZCQ1, CQ1T1, TERMCQ1, ZCF1, CF1T1, TERMCF1,
     & ZHK1, HK1T1, TERMHK1, ROW12,
     & ZDE2, DE2T2, TERMUPW2, TERMDE2, TERMUS2, ROW22TRANSPORT,
     & ZCQ2, CQ2T2, TERMCQ2, ZCF2, CF2T2, TERMCF2,
     & ZHK2, HK2T2, TERMHK2, ROW22
 1000 FORMAT('{"sequence":',I12,',"runtime":"fortran",'
     & '"kind":"bldif_eq1_t_terms","scope":"',A,
     & '","name":null,"data":{"ityp":',I2,
     & ',"zDe1":',1PE24.16,',"de1T1":',1PE24.16,
     & ',"upwT1Term":',1PE24.16,',"de1T1Term":',1PE24.16,
     & ',"us1T1Term":',1PE24.16,',"row12Transport":',1PE24.16,
     & ',"zCq1":',1PE24.16,',"cq1T1":',1PE24.16,
     & ',"cq1T1Term":',1PE24.16,
     & ',"zCf1":',1PE24.16,',"cf1T1":',1PE24.16,
     & ',"cf1T1Term":',1PE24.16,
     & ',"zHk1":',1PE24.16,',"hk1T1":',1PE24.16,
     & ',"hk1T1Term":',1PE24.16,',"row12":',1PE24.16,
     & ',"zDe2":',1PE24.16,',"de2T2":',1PE24.16,
     & ',"upwT2Term":',1PE24.16,',"de2T2Term":',1PE24.16,
     & ',"us2T2Term":',1PE24.16,',"row22Transport":',1PE24.16,
     & ',"zCq2":',1PE24.16,',"cq2T2":',1PE24.16,
     & ',"cq2T2Term":',1PE24.16,
     & ',"zCf2":',1PE24.16,',"cf2T2":',1PE24.16,
     & ',"cf2T2Term":',1PE24.16,
     & ',"zHk2":',1PE24.16,',"hk2T2":',1PE24.16,
     & ',"hk2T2Term":',1PE24.16,',"row22":',1PE24.16,
     & '},"values":null,"tags":null,"timestampUtc":null}')
      RETURN
      END


      SUBROUTINE TRACE_BLDIF_EQ1_S_TERMS(SCOPE, ITYP,
     & ONEMINUSUPW, UPW, ZSA, ZSL, S1VAL, S2VAL,
     & ROW11STERM, ROW11LTERM, ROW11,
     & ROW21STERM, ROW21LTERM, ROW21)
      CHARACTER*(*) SCOPE
      INTEGER TRACE_LENTRIM, TRACE_NEXTSEQ
      INTEGER LS, SEQ, ITYP
      REAL ONEMINUSUPW, UPW, ZSA, ZSL, S1VAL, S2VAL
      REAL ROW11STERM, ROW11LTERM, ROW11
      REAL ROW21STERM, ROW21LTERM, ROW21
      LOGICAL LOPEN
      CHARACTER*128 CSCOPE
C
      INQUIRE(UNIT=51, OPENED=LOPEN)
      IF(.NOT.LOPEN) RETURN
      CALL TRACE_CLEAN(SCOPE, CSCOPE)
      LS = TRACE_LENTRIM(CSCOPE)
      IF(LS.LE.0) LS = 1
C
      SEQ = TRACE_NEXTSEQ()
      WRITE(51,1000) SEQ, CSCOPE(1:LS), ITYP,
     & ONEMINUSUPW, UPW, ZSA, ZSL, S1VAL, S2VAL,
     & ROW11STERM, ROW11LTERM, ROW11,
     & ROW21STERM, ROW21LTERM, ROW21
 1000 FORMAT('{"sequence":',I12,',"runtime":"fortran",'
     & '"kind":"bldif_eq1_s_terms","scope":"',A,
     & '","name":null,"data":{"ityp":',I2,
     & ',"oneMinusUpw":',1PE24.16,',"upw":',1PE24.16,
     & ',"zSa":',1PE24.16,',"zSl":',1PE24.16,
     & ',"s1":',1PE24.16,',"s2":',1PE24.16,
     & ',"row11StoredTerm":',1PE24.16,
     & ',"row11LogTerm":',1PE24.16,
     & ',"row11":',1PE24.16,
     & ',"row21StoredTerm":',1PE24.16,
     & ',"row21LogTerm":',1PE24.16,
     & ',"row21":',1PE24.16,
     & '},"values":null,"tags":null,"timestampUtc":null}')
      RETURN
      END


      SUBROUTINE TRACE_BLDIF_EQ1_ROWS(SCOPE, ITYP,
     & ROW11, ROW12, ROW13, ROW14, ROW21, ROW22, ROW23, ROW24)
      CHARACTER*(*) SCOPE
      INTEGER TRACE_LENTRIM, TRACE_NEXTSEQ
      INTEGER LS, SEQ, ITYP
      REAL ROW11, ROW12, ROW13, ROW14, ROW21, ROW22, ROW23, ROW24
      LOGICAL LOPEN
      CHARACTER*128 CSCOPE
C
      INQUIRE(UNIT=51, OPENED=LOPEN)
      IF(.NOT.LOPEN) RETURN
      CALL TRACE_CLEAN(SCOPE, CSCOPE)
      LS = TRACE_LENTRIM(CSCOPE)
      IF(LS.LE.0) LS = 1
C
      SEQ = TRACE_NEXTSEQ()
      WRITE(51,1000) SEQ, CSCOPE(1:LS), ITYP,
     &               ROW11, ROW12, ROW13, ROW14,
     &               ROW21, ROW22, ROW23, ROW24
 1000 FORMAT('{"sequence":',I12,',"runtime":"fortran",'
     & '"kind":"bldif_eq1_rows","scope":"',A,
     & '","name":null,"data":{"ityp":',I2,
     & ',"row11":',1PE24.16,',"row12":',1PE24.16,
     & ',"row13":',1PE24.16,',"row14":',1PE24.16,
     & ',"row21":',1PE24.16,',"row22":',1PE24.16,
     & ',"row23":',1PE24.16,',"row24":',1PE24.16,
     & '},"values":null,"tags":null,"timestampUtc":null}')
      RETURN
      END


      SUBROUTINE TRACE_BLDIF_EQ2_INPUT_BUNDLE(SCOPE, SIDE, STATION,
     & ITYP,
     & X1VAL, X2VAL, U1VAL, U2VAL, T1VAL, T2VAL, DW1VAL, DW2VAL,
     & H1VAL, H1T1VAL, H1D1VAL, H2VAL, H2T2VAL, H2D2VAL,
     & M1VAL, M1U1VAL, M2VAL, M2U2VAL,
     & CFMVAL, CFMT1VAL, CFMD1VAL, CFMU1VAL,
     & CFMT2VAL, CFMD2VAL, CFMU2VAL,
     & CF1VAL, CF1T1VAL, CF1D1VAL, CF1U1VAL,
     & CF2VAL, CF2T2VAL, CF2D2VAL, CF2U2VAL,
     & XLOGVAL, ULOGVAL, TLOGVAL, DDLOGVAL)
      CHARACTER*(*) SCOPE
      INTEGER TRACE_LENTRIM, TRACE_NEXTSEQ
      INTEGER LS, SEQ, SIDE, STATION, ITYP
      REAL X1VAL, X2VAL, U1VAL, U2VAL, T1VAL, T2VAL, DW1VAL, DW2VAL
      REAL H1VAL, H1T1VAL, H1D1VAL, H2VAL, H2T2VAL, H2D2VAL
      REAL M1VAL, M1U1VAL, M2VAL, M2U2VAL
      REAL CFMVAL, CFMT1VAL, CFMD1VAL, CFMU1VAL
      REAL CFMT2VAL, CFMD2VAL, CFMU2VAL
      REAL CF1VAL, CF1T1VAL, CF1D1VAL, CF1U1VAL
      REAL CF2VAL, CF2T2VAL, CF2D2VAL, CF2U2VAL
      REAL XLOGVAL, ULOGVAL, TLOGVAL, DDLOGVAL
      LOGICAL LOPEN
      CHARACTER*128 CSCOPE
C
      INQUIRE(UNIT=51, OPENED=LOPEN)
      IF(.NOT.LOPEN) RETURN
      CALL TRACE_CLEAN(SCOPE, CSCOPE)
      LS = TRACE_LENTRIM(CSCOPE)
      IF(LS.LE.0) LS = 1
C
      SEQ = TRACE_NEXTSEQ()
      WRITE(51,1000) SEQ, CSCOPE(1:LS), SIDE, STATION, ITYP,
     & X1VAL, X2VAL, U1VAL, U2VAL, T1VAL, T2VAL, DW1VAL, DW2VAL,
     & H1VAL, H1T1VAL, H1D1VAL, H2VAL, H2T2VAL, H2D2VAL,
     & M1VAL, M1U1VAL, M2VAL, M2U2VAL,
     & CFMVAL, CFMT1VAL, CFMD1VAL, CFMU1VAL,
     & CFMT2VAL, CFMD2VAL, CFMU2VAL,
     & CF1VAL, CF1T1VAL, CF1D1VAL, CF1U1VAL,
     & CF2VAL, CF2T2VAL, CF2D2VAL, CF2U2VAL,
     & XLOGVAL, ULOGVAL, TLOGVAL, DDLOGVAL
 1000 FORMAT('{"sequence":',I12,',"runtime":"fortran",'
     & '"kind":"bldif_eq2_input_bundle","scope":"',A,
     & '","name":null,"data":{"side":',I8,',"station":',I8,
     & ',"ityp":',I2,
     & ',"x1":',1PE24.16,',"x2":',1PE24.16,
     & ',"u1":',1PE24.16,',"u2":',1PE24.16,
     & ',"t1":',1PE24.16,',"t2":',1PE24.16,
     & ',"dw1":',1PE24.16,',"dw2":',1PE24.16,
     & ',"h1":',1PE24.16,',"h1T1":',1PE24.16,
     & ',"h1D1":',1PE24.16,',"h2":',1PE24.16,
     & ',"h2T2":',1PE24.16,',"h2D2":',1PE24.16,
     & ',"m1":',1PE24.16,',"m1U1":',1PE24.16,
     & ',"m2":',1PE24.16,',"m2U2":',1PE24.16,
     & ',"cfm":',1PE24.16,',"cfmT1":',1PE24.16,
     & ',"cfmD1":',1PE24.16,',"cfmU1":',1PE24.16,
     & ',"cfmT2":',1PE24.16,',"cfmD2":',1PE24.16,
     & ',"cfmU2":',1PE24.16,',"cf1":',1PE24.16,
     & ',"cf1T1":',1PE24.16,',"cf1D1":',1PE24.16,
     & ',"cf1U1":',1PE24.16,',"cf2":',1PE24.16,
     & ',"cf2T2":',1PE24.16,',"cf2D2":',1PE24.16,
     & ',"cf2U2":',1PE24.16,',"xlog":',1PE24.16,
     & ',"ulog":',1PE24.16,',"tlog":',1PE24.16,
     & ',"ddlog":',1PE24.16,',"legacy":true'
     & '},"values":null,"tags":null,"timestampUtc":null}')
      RETURN
      END


      SUBROUTINE TRACE_BLDIF_EQ2_RESIDUAL_TERMS(SCOPE, ITYP,
     & HAVAL, MAVAL, XAVAL, TAVAL, HWAVAL,
     & CFXCENVAL, CFXPANVAL, CFXVAL, BTMPVAL,
     & TLOGVAL, ULOGVAL, XLOGVAL, REZTVAL)
      CHARACTER*(*) SCOPE
      INTEGER TRACE_LENTRIM, TRACE_NEXTSEQ
      INTEGER LS, SEQ, ITYP
      REAL HAVAL, MAVAL, XAVAL, TAVAL, HWAVAL
      REAL CFXCENVAL, CFXPANVAL, CFXVAL, BTMPVAL
      REAL TLOGVAL, ULOGVAL, XLOGVAL, REZTVAL
      LOGICAL LOPEN
      CHARACTER*128 CSCOPE
C
      INQUIRE(UNIT=51, OPENED=LOPEN)
      IF(.NOT.LOPEN) RETURN
      CALL TRACE_CLEAN(SCOPE, CSCOPE)
      LS = TRACE_LENTRIM(CSCOPE)
      IF(LS.LE.0) LS = 1
C
      SEQ = TRACE_NEXTSEQ()
      WRITE(51,1000) SEQ, CSCOPE(1:LS), ITYP,
     &               HAVAL, MAVAL, XAVAL, TAVAL, HWAVAL,
     &               CFXCENVAL, CFXPANVAL, CFXVAL, BTMPVAL,
     &               TLOGVAL, ULOGVAL, XLOGVAL, REZTVAL
 1000 FORMAT('{"sequence":',I12,',"runtime":"fortran",'
     & '"kind":"bldif_eq2_residual_terms","scope":"',A,
     & '","name":null,"data":{"ityp":',I2,
     & ',"ha":',1PE24.16,',"ma":',1PE24.16,
     & ',"xa":',1PE24.16,',"ta":',1PE24.16,
     & ',"hwa":',1PE24.16,',"cfxCenter":',1PE24.16,
     & ',"cfxPanels":',1PE24.16,',"cfx":',1PE24.16,
     & ',"btmp":',1PE24.16,',"tlog":',1PE24.16,
     & ',"ulog":',1PE24.16,',"xlog":',1PE24.16,
     & ',"rezt":',1PE24.16,
     & '},"values":null,"tags":null,"timestampUtc":null}')
      RETURN
      END


      SUBROUTINE TRACE_BLDIF_EQ2_ZT2_TERMS(SCOPE,
     & ZTLVAL, T2VAL, ZCFXVAL, CFXT2VAL, ZHWAVAL, DW2VAL,
     & ZT2LOGVAL, ZT2CFXVAL, ZT2HWAVAL, ZT2VAL)
      CHARACTER*(*) SCOPE
      INTEGER TRACE_LENTRIM, TRACE_NEXTSEQ
      INTEGER LS, SEQ
      REAL ZTLVAL, T2VAL, ZCFXVAL, CFXT2VAL, ZHWAVAL, DW2VAL
      REAL ZT2LOGVAL, ZT2CFXVAL, ZT2HWAVAL, ZT2VAL
      LOGICAL LOPEN
      CHARACTER*128 CSCOPE
C
      INQUIRE(UNIT=51, OPENED=LOPEN)
      IF(.NOT.LOPEN) RETURN
      CALL TRACE_CLEAN(SCOPE, CSCOPE)
      LS = TRACE_LENTRIM(CSCOPE)
      IF(LS.LE.0) LS = 1
C
      SEQ = TRACE_NEXTSEQ()
      WRITE(51,1000) SEQ, CSCOPE(1:LS),
     &               ZTLVAL, T2VAL, ZCFXVAL, CFXT2VAL,
     &               ZHWAVAL, DW2VAL,
     &               ZT2LOGVAL, ZT2CFXVAL, ZT2HWAVAL, ZT2VAL
 1000 FORMAT('{"sequence":',I12,',"runtime":"fortran",'
     & '"kind":"bldif_eq2_zt2_terms","scope":"',A,
     & '","name":null,"data":{"zTl":',1PE24.16,
     & ',"t2":',1PE24.16,',"zCfx":',1PE24.16,
     & ',"cfxT2":',1PE24.16,',"zHwa":',1PE24.16,
     & ',"dw2":',1PE24.16,',"zT2Log":',1PE24.16,
     & ',"zT2Cfx":',1PE24.16,',"zT2Hwa":',1PE24.16,
     & ',"zT2":',1PE24.16,
     & '},"values":null,"tags":null,"timestampUtc":null}')
      RETURN
      END


      SUBROUTINE TRACE_BLDIF_EQ2_D1_TERMS(SCOPE,
     & SIDE, STATION, ITYP,
     & ZHAHALFVAL, ZCFMVAL, ZCF1VAL,
     & H1D1VAL, CFMD1VAL, CF1D1VAL,
     & VS1ROW23HAVAL, VS1ROW23CFMVAL, VS1ROW23CFVAL, VS1ROW23VAL)
      CHARACTER*(*) SCOPE
      INTEGER TRACE_LENTRIM, TRACE_NEXTSEQ
      INTEGER LS, SEQ, SIDE, STATION, ITYP
      REAL ZHAHALFVAL, ZCFMVAL, ZCF1VAL
      REAL H1D1VAL, CFMD1VAL, CF1D1VAL
      REAL VS1ROW23HAVAL, VS1ROW23CFMVAL, VS1ROW23CFVAL, VS1ROW23VAL
      LOGICAL LOPEN
      CHARACTER*128 CSCOPE
C
      INQUIRE(UNIT=51, OPENED=LOPEN)
      IF(.NOT.LOPEN) RETURN
      CALL TRACE_CLEAN(SCOPE, CSCOPE)
      LS = TRACE_LENTRIM(CSCOPE)
      IF(LS.LE.0) LS = 1
C
      SEQ = TRACE_NEXTSEQ()
      WRITE(51,1000) SEQ, CSCOPE(1:LS), SIDE, STATION, ITYP,
     &               ZHAHALFVAL, ZCFMVAL, ZCF1VAL,
     &               H1D1VAL, CFMD1VAL, CF1D1VAL,
     &               VS1ROW23HAVAL, VS1ROW23CFMVAL, VS1ROW23CFVAL,
     &               VS1ROW23VAL
 1000 FORMAT('{"sequence":',I12,',"runtime":"fortran",'
     & '"kind":"bldif_eq2_d1_terms","scope":"',A,
     & '","name":null,"data":{"side":',I3,
     & ',"station":',I4,',"ityp":',I2,
     & ',"zHaHalf":',1PE24.16,',"zCfm":',1PE24.16,
     & ',"zCf1":',1PE24.16,',"h1D1":',1PE24.16,
     & ',"cfmD1":',1PE24.16,',"cf1D1":',1PE24.16,
     & ',"vs1Row23Ha":',1PE24.16,',"vs1Row23Cfm":',1PE24.16,
     & ',"vs1Row23Cf":',1PE24.16,',"vs1Row23":',1PE24.16,
     & '},"values":null,"tags":null,"timestampUtc":null}')
      RETURN
      END


      SUBROUTINE TRACE_BLDIF_EQ2_D2_TERMS(SCOPE,
     & ZHAHALFVAL, ZCFMVAL, ZCF2VAL,
     & H2D2VAL, CFMD2VAL, CF2D2VAL,
     & ROW23HAVAL, ROW23CFMVAL, ROW23CFVAL, ROW23VAL)
      CHARACTER*(*) SCOPE
      INTEGER TRACE_LENTRIM, TRACE_NEXTSEQ
      INTEGER LS, SEQ
      REAL ZHAHALFVAL, ZCFMVAL, ZCF2VAL
      REAL H2D2VAL, CFMD2VAL, CF2D2VAL
      REAL ROW23HAVAL, ROW23CFMVAL, ROW23CFVAL, ROW23VAL
      LOGICAL LOPEN
      CHARACTER*128 CSCOPE
C
      INQUIRE(UNIT=51, OPENED=LOPEN)
      IF(.NOT.LOPEN) RETURN
      CALL TRACE_CLEAN(SCOPE, CSCOPE)
      LS = TRACE_LENTRIM(CSCOPE)
      IF(LS.LE.0) LS = 1
C
      SEQ = TRACE_NEXTSEQ()
      WRITE(51,1000) SEQ, CSCOPE(1:LS),
     &               ZHAHALFVAL, ZCFMVAL, ZCF2VAL,
     &               H2D2VAL, CFMD2VAL, CF2D2VAL,
     &               ROW23HAVAL, ROW23CFMVAL, ROW23CFVAL, ROW23VAL
 1000 FORMAT('{"sequence":',I12,',"runtime":"fortran",'
     & '"kind":"bldif_eq2_d2_terms","scope":"',A,
     & '","name":null,"data":{"zHaHalf":',1PE24.16,
     & ',"zCfm":',1PE24.16,',"zCf2":',1PE24.16,
     & ',"h2D2":',1PE24.16,',"cfmD2":',1PE24.16,
     & ',"cf2D2":',1PE24.16,',"row23Ha":',1PE24.16,
     & ',"row23Cfm":',1PE24.16,',"row23Cf":',1PE24.16,
     & ',"row23":',1PE24.16,
     & '},"values":null,"tags":null,"timestampUtc":null}')
      RETURN
      END


      SUBROUTINE TRACE_BLDIF_EQ2_U_TERMS(SCOPE,
     & ZCFXVAL, CFXCFMVAL, CFXCF1VAL, CFXCF2VAL,
     & ZCFMVAL, ZCF1VAL, ZCF2VAL,
     & CFMU1VAL, CFMU2VAL, CF1U1VAL, CF2U2VAL,
     & ROW14MA, ROW14CFM, ROW14CF, ROW14U, ROW14,
     & ROW24MA, ROW24CFM, ROW24CF, ROW24U, ROW24)
      CHARACTER*(*) SCOPE
      INTEGER TRACE_LENTRIM, TRACE_NEXTSEQ
      INTEGER LS, SEQ
      REAL ZCFXVAL, CFXCFMVAL, CFXCF1VAL, CFXCF2VAL
      REAL ZCFMVAL, ZCF1VAL, ZCF2VAL
      REAL CFMU1VAL, CFMU2VAL, CF1U1VAL, CF2U2VAL
      REAL ROW14MA, ROW14CFM, ROW14CF, ROW14U, ROW14
      REAL ROW24MA, ROW24CFM, ROW24CF, ROW24U, ROW24
      LOGICAL LOPEN
      CHARACTER*128 CSCOPE
C
      INQUIRE(UNIT=51, OPENED=LOPEN)
      IF(.NOT.LOPEN) RETURN
      CALL TRACE_CLEAN(SCOPE, CSCOPE)
      LS = TRACE_LENTRIM(CSCOPE)
      IF(LS.LE.0) LS = 1
C
      SEQ = TRACE_NEXTSEQ()
      WRITE(51,1000) SEQ, CSCOPE(1:LS),
     &               ZCFXVAL, CFXCFMVAL, CFXCF1VAL, CFXCF2VAL,
     &               ZCFMVAL, ZCF1VAL, ZCF2VAL,
     &               CFMU1VAL, CFMU2VAL, CF1U1VAL, CF2U2VAL,
     &               ROW14MA, ROW14CFM, ROW14CF, ROW14U, ROW14,
     &               ROW24MA, ROW24CFM, ROW24CF, ROW24U, ROW24
 1000 FORMAT('{"sequence":',I12,',"runtime":"fortran",'
     & '"kind":"bldif_eq2_u_terms","scope":"',A,
     & '","name":null,"data":{"zCfx":',1PE24.16,
     & ',"cfxCfm":',1PE24.16,',"cfxCf1":',1PE24.16,
     & ',"cfxCf2":',1PE24.16,',"zCfm":',1PE24.16,
     & ',"zCf1":',1PE24.16,',"zCf2":',1PE24.16,
     & ',"cfmU1":',1PE24.16,',"cfmU2":',1PE24.16,
     & ',"cf1U1":',1PE24.16,',"cf2U2":',1PE24.16,
     & ',"row14Ma":',1PE24.16,
     & ',"row14Cfm":',1PE24.16,',"row14Cf":',1PE24.16,
     & ',"row14U":',1PE24.16,',"row14":',1PE24.16,
     & ',"row24Ma":',1PE24.16,',"row24Cfm":',1PE24.16,
     & ',"row24Cf":',1PE24.16,',"row24U":',1PE24.16,
     & ',"row24":',1PE24.16,
     & '},"values":null,"tags":null,"timestampUtc":null}')
      RETURN
      END

      SUBROUTINE TRACE_BLDIF_EQ2_T1_TERMS(SCOPE,
     & ZHAHALFVAL, ZCFMVAL, ZCF1VAL, ZT1VAL,
     & H1T1VAL, CFMT1VAL, CF1T1VAL,
     & VS1ROW22HAVAL, VS1ROW22CFMVAL, VS1ROW22CFVAL, VS1ROW22VAL)
      CHARACTER*(*) SCOPE
      INTEGER TRACE_LENTRIM, TRACE_NEXTSEQ
      INTEGER LS, SEQ
      REAL ZHAHALFVAL, ZCFMVAL, ZCF1VAL, ZT1VAL
      REAL H1T1VAL, CFMT1VAL, CF1T1VAL
      REAL VS1ROW22HAVAL, VS1ROW22CFMVAL, VS1ROW22CFVAL, VS1ROW22VAL
      LOGICAL LOPEN
      CHARACTER*128 CSCOPE
C
      INQUIRE(UNIT=51, OPENED=LOPEN)
      IF(.NOT.LOPEN) RETURN
      CALL TRACE_CLEAN(SCOPE, CSCOPE)
      LS = TRACE_LENTRIM(CSCOPE)
      IF(LS.LE.0) LS = 1
C
      SEQ = TRACE_NEXTSEQ()
      WRITE(51,1000) SEQ, CSCOPE(1:LS),
     &               ZHAHALFVAL, ZCFMVAL, ZCF1VAL, ZT1VAL,
     &               H1T1VAL, CFMT1VAL, CF1T1VAL,
     &               VS1ROW22HAVAL, VS1ROW22CFMVAL, VS1ROW22CFVAL,
     &               VS1ROW22VAL
 1000 FORMAT('{"sequence":',I12,',"runtime":"fortran",'
     & '"kind":"bldif_eq2_t1_terms","scope":"',A,
     & '","name":null,"data":{"zHaHalf":',1PE24.16,
     & ',"zCfm":',1PE24.16,',"zCf1":',1PE24.16,
     & ',"zT1":',1PE24.16,',"h1T1":',1PE24.16,
     & ',"cfmT1":',1PE24.16,',"cf1T1":',1PE24.16,
     & ',"vs1Row22Ha":',1PE24.16,',"vs1Row22Cfm":',1PE24.16,
     & ',"vs1Row22Cf":',1PE24.16,',"vs1Row22":',1PE24.16,
     & '},"values":null,"tags":null,"timestampUtc":null}')
      RETURN
      END

      SUBROUTINE TRACE_BLDIF_EQ2_T2_TERMS(SCOPE,
     & SIDE, STATION, ITYP,
     & ZHAHALFVAL, ZCFMVAL, ZCF2VAL, ZT2VAL,
     & H2T2VAL, CFMT2VAL, CF2T2VAL,
     & ROW22HAVAL, ROW22CFMVAL, ROW22CFVAL, ROW22VAL)
      CHARACTER*(*) SCOPE
      INTEGER TRACE_LENTRIM, TRACE_NEXTSEQ
      INTEGER LS, SEQ, SIDE, STATION, ITYP
      REAL ZHAHALFVAL, ZCFMVAL, ZCF2VAL, ZT2VAL
      REAL H2T2VAL, CFMT2VAL, CF2T2VAL
      REAL ROW22HAVAL, ROW22CFMVAL, ROW22CFVAL, ROW22VAL
      LOGICAL LOPEN
      CHARACTER*128 CSCOPE
C
      INQUIRE(UNIT=51, OPENED=LOPEN)
      IF(.NOT.LOPEN) RETURN
      CALL TRACE_CLEAN(SCOPE, CSCOPE)
      LS = TRACE_LENTRIM(CSCOPE)
      IF(LS.LE.0) LS = 1
C
      SEQ = TRACE_NEXTSEQ()
      WRITE(51,1000) SEQ, CSCOPE(1:LS), SIDE, STATION, ITYP,
     &               ZHAHALFVAL, ZCFMVAL, ZCF2VAL, ZT2VAL,
     &               H2T2VAL, CFMT2VAL, CF2T2VAL,
     &               ROW22HAVAL, ROW22CFMVAL, ROW22CFVAL, ROW22VAL
 1000 FORMAT('{"sequence":',I12,',"runtime":"fortran",'
     & '"kind":"bldif_eq2_t2_terms","scope":"',A,
     & '","name":null,"data":{"side":',I8,',"station":',I8,
     & ',"ityp":',I2,
     & ',"zHaHalf":',1PE24.16,
     & ',"zCfm":',1PE24.16,',"zCf2":',1PE24.16,
     & ',"zT2":',1PE24.16,',"h2T2":',1PE24.16,
     & ',"cfmT2":',1PE24.16,',"cf2T2":',1PE24.16,
     & ',"row22Ha":',1PE24.16,',"row22Cfm":',1PE24.16,
     & ',"row22Cf":',1PE24.16,',"row22":',1PE24.16,
     & '},"values":null,"tags":null,"timestampUtc":null}')
      RETURN
      END


      SUBROUTINE TRACE_TRANSITION_DAMPL_TERMS(SCOPE,
     & HKVAL, THVAL, RTVAL, GRVAL, GRCRITVAL, RFACVAL,
     & DADRVAL, AFVAL, AXVAL, AXHKVAL, AXTHVAL, AXRTVAL)
      CHARACTER*(*) SCOPE
      INTEGER TRACE_LENTRIM, TRACE_NEXTSEQ
      INTEGER LS, SEQ
      REAL HKVAL, THVAL, RTVAL, GRVAL, GRCRITVAL, RFACVAL
      REAL DADRVAL, AFVAL, AXVAL, AXHKVAL, AXTHVAL, AXRTVAL
      LOGICAL LOPEN
      CHARACTER*128 CSCOPE
C
      INQUIRE(UNIT=51, OPENED=LOPEN)
      IF(.NOT.LOPEN) RETURN
      CALL TRACE_CLEAN(SCOPE, CSCOPE)
      LS = TRACE_LENTRIM(CSCOPE)
      IF(LS.LE.0) LS = 1
C
      SEQ = TRACE_NEXTSEQ()
      WRITE(51,1000) SEQ, CSCOPE(1:LS),
     &               HKVAL, THVAL, RTVAL, GRVAL, GRCRITVAL,
     &               RFACVAL, DADRVAL, AFVAL, AXVAL,
     &               AXHKVAL, AXTHVAL, AXRTVAL
 1000 FORMAT('{"sequence":',I12,',"runtime":"fortran",'
     & '"kind":"transition_dampl_terms","scope":"',A,
     & '","name":null,"data":{"hk":',1PE24.16,
     & ',"th":',1PE24.16,',"rt":',1PE24.16,
     & ',"gr":',1PE24.16,',"grcrit":',1PE24.16,
     & ',"rfac":',1PE24.16,',"dadr":',1PE24.16,
     & ',"af":',1PE24.16,',"ax":',1PE24.16,
     & ',"axHk":',1PE24.16,',"axTh":',1PE24.16,
     & ',"axRt":',1PE24.16,
     & '},"values":null,"tags":null,"timestampUtc":null}')
      RETURN
      END


      SUBROUTINE TRACE_TRANSITION_DAMPL_POLY_TERMS(SCOPE,
     & HKVAL, HMIVAL, HMI2VAL, HMI3VAL,
     & TERM1VAL, TERM2VAL, TERM3VAL, TERM4VAL, AFVAL)
      CHARACTER*(*) SCOPE
      INTEGER TRACE_LENTRIM, TRACE_NEXTSEQ
      INTEGER LS, SEQ
      REAL HKVAL, HMIVAL, HMI2VAL, HMI3VAL
      REAL TERM1VAL, TERM2VAL, TERM3VAL, TERM4VAL, AFVAL
      LOGICAL LOPEN
      CHARACTER*128 CSCOPE
C
      INQUIRE(UNIT=51, OPENED=LOPEN)
      IF(.NOT.LOPEN) RETURN
      CALL TRACE_CLEAN(SCOPE, CSCOPE)
      LS = TRACE_LENTRIM(CSCOPE)
      IF(LS.LE.0) LS = 1
C
      SEQ = TRACE_NEXTSEQ()
      WRITE(51,1000) SEQ, CSCOPE(1:LS),
     &               HKVAL, HMIVAL, HMI2VAL, HMI3VAL,
     &               TERM1VAL, TERM2VAL, TERM3VAL, TERM4VAL, AFVAL
 1000 FORMAT('{"sequence":',I12,',"runtime":"fortran",'
     & '"kind":"transition_dampl_poly_terms","scope":"',A,
     & '","name":null,"data":{"hk":',1PE24.16,
     & ',"hmi":',1PE24.16,',"hmi2":',1PE24.16,
     & ',"hmi3":',1PE24.16,',"term1":',1PE24.16,
     & ',"term2":',1PE24.16,',"term3":',1PE24.16,
     & ',"term4":',1PE24.16,',"af":',1PE24.16,
     & '},"values":null,"tags":null,"timestampUtc":null}')
      RETURN
      END


      SUBROUTINE TRACE_TRANSITION_DAMPL_DERIVATIVE_TERMS(SCOPE,
     & HKVAL, HMIVAL, HMI2VAL, HMIHKVAL, BBVAL, ONEMINUSBBSQVAL,
     & AAHKVAL, BBHKVAL, GRCHKVAL, GRRTVAL,
     & RNHKVAL, RNRTVAL, RFACHKVAL, RFACRTVAL,
     & ARGHKVAL, EXHKVAL, DADRHKVAL, AFHMIVAL, AFHKVAL,
     & AFDADROVERTHVAL, AXHKBASEVAL, AXHKVAL, AXRTVAL)
      CHARACTER*(*) SCOPE
      INTEGER TRACE_LENTRIM, TRACE_NEXTSEQ
      INTEGER LS, SEQ
      REAL HKVAL, HMIVAL, HMI2VAL, HMIHKVAL, BBVAL, ONEMINUSBBSQVAL
      REAL AAHKVAL, BBHKVAL, GRCHKVAL, GRRTVAL
      REAL RNHKVAL, RNRTVAL, RFACHKVAL, RFACRTVAL
      REAL ARGHKVAL, EXHKVAL, DADRHKVAL, AFHMIVAL, AFHKVAL
      REAL AFDADROVERTHVAL, AXHKBASEVAL, AXHKVAL, AXRTVAL
      LOGICAL LOPEN
      CHARACTER*128 CSCOPE
C
      INQUIRE(UNIT=51, OPENED=LOPEN)
      IF(.NOT.LOPEN) RETURN
      CALL TRACE_CLEAN(SCOPE, CSCOPE)
      LS = TRACE_LENTRIM(CSCOPE)
      IF(LS.LE.0) LS = 1
C
      SEQ = TRACE_NEXTSEQ()
      WRITE(51,1000) SEQ, CSCOPE(1:LS),
     &               HKVAL, HMIVAL, HMI2VAL, HMIHKVAL,
     &               BBVAL, ONEMINUSBBSQVAL, AAHKVAL, BBHKVAL,
     &               GRCHKVAL, GRRTVAL, RNHKVAL, RNRTVAL,
     &               RFACHKVAL, RFACRTVAL, ARGHKVAL, EXHKVAL,
     &               DADRHKVAL, AFHMIVAL, AFHKVAL,
     &               AFDADROVERTHVAL, AXHKBASEVAL, AXHKVAL, AXRTVAL
 1000 FORMAT('{"sequence":',I12,',"runtime":"fortran",'
     & '"kind":"transition_dampl_derivative_terms","scope":"',A,
     & '","name":null,"data":{"hk":',1PE24.16,
     & ',"hmi":',1PE24.16,',"hmi2":',1PE24.16,
     & ',"hmiHk":',1PE24.16,',"bb":',1PE24.16,
     & ',"oneMinusBbSq":',1PE24.16,',"aaHk":',1PE24.16,
     & ',"bbHk":',1PE24.16,',"grcHk":',1PE24.16,
     & ',"grRt":',1PE24.16,',"rnHk":',1PE24.16,
     & ',"rnRt":',1PE24.16,',"rfacHk":',1PE24.16,
     & ',"rfacRt":',1PE24.16,',"argHk":',1PE24.16,
     & ',"exHk":',1PE24.16,',"dadrHk":',1PE24.16,
     & ',"afHmi":',1PE24.16,',"afHk":',1PE24.16,
     & ',"afdadrOverTh":',1PE24.16,',"axHkBase":',1PE24.16,
     & ',"axHk":',1PE24.16,',"axRt":',1PE24.16,
     & '},"values":null,"tags":null,"timestampUtc":null}')
      RETURN
      END


      SUBROUTINE TRACE_TRANSITION_AXSET_TERMS(SCOPE,
     & AX1VAL, AX2VAL, AX1SQVAL, AX2SQVAL, AXSUMVAL,
     & AXSQVAL, AXAVAL, AXAAX1VAL, AXAAX2VAL,
     & ARGVAL, EXNVAL, DAXVAL, DAXA1VAL, DAXA2VAL, DAXT1VAL,
     & DAXT2VAL, AXFINALVAL)
      CHARACTER*(*) SCOPE
      INTEGER TRACE_LENTRIM, TRACE_NEXTSEQ
      INTEGER LS, SEQ
      REAL AX1VAL, AX2VAL, AX1SQVAL, AX2SQVAL, AXSUMVAL
      REAL AXSQVAL, AXAVAL, AXAAX1VAL, AXAAX2VAL
      REAL ARGVAL, EXNVAL, DAXVAL, DAXA1VAL, DAXA2VAL, DAXT1VAL
      REAL DAXT2VAL, AXFINALVAL
      LOGICAL LOPEN
      CHARACTER*128 CSCOPE
C
      INQUIRE(UNIT=51, OPENED=LOPEN)
      IF(.NOT.LOPEN) RETURN
      CALL TRACE_CLEAN(SCOPE, CSCOPE)
      LS = TRACE_LENTRIM(CSCOPE)
      IF(LS.LE.0) LS = 1
C
      SEQ = TRACE_NEXTSEQ()
      WRITE(51,1000) SEQ, CSCOPE(1:LS),
     &               AX1VAL, AX2VAL, AX1SQVAL, AX2SQVAL, AXSUMVAL,
     &               AXSQVAL, AXAVAL,
     &               AXAAX1VAL, AXAAX2VAL,
     &               ARGVAL, EXNVAL, DAXVAL, DAXA1VAL, DAXA2VAL,
     &               DAXT1VAL, DAXT2VAL, AXFINALVAL
 1000 FORMAT('{"sequence":',I12,',"runtime":"fortran",'
     & '"kind":"transition_axset_terms","scope":"',A,
     & '","name":null,"data":{"ax1":',1PE24.16,
     & ',"ax2":',1PE24.16,',"ax1Sq":',1PE24.16,
     & ',"ax2Sq":',1PE24.16,',"axSum":',1PE24.16,
     & ',"axsq":',1PE24.16,
     & ',"axa":',1PE24.16,',"axaAx1":',1PE24.16,
     & ',"axaAx2":',1PE24.16,',"arg":',1PE24.16,
     & ',"exn":',1PE24.16,',"dax":',1PE24.16,
     & ',"daxA1":',1PE24.16,',"daxA2":',1PE24.16,
     & ',"daxT1":',1PE24.16,',"daxT2":',1PE24.16,
     & ',"axFinal":',1PE24.16,
     & '},"values":null,"tags":null,"timestampUtc":null}')
      RETURN
      END


      SUBROUTINE TRACE_TRANSITION_POINT_SEED(SCOPE, X1, X2, DX,
     & HK1VAL, T1VAL, RT1VAL, A1VAL,
     & HK2VAL, T2VAL, RT2VAL, A2INVAL, ACRITVAL, IDAMPVAL,
     & AX0VAL, A2SEEDVAL)
      CHARACTER*(*) SCOPE
      INTEGER TRACE_LENTRIM, TRACE_NEXTSEQ
      INTEGER LS, SEQ, IDAMPVAL
      REAL X1, X2, DX
      REAL HK1VAL, T1VAL, RT1VAL, A1VAL
      REAL HK2VAL, T2VAL, RT2VAL, A2INVAL, ACRITVAL
      REAL AX0VAL, A2SEEDVAL
      LOGICAL LOPEN
      CHARACTER*128 CSCOPE
C
      INQUIRE(UNIT=51, OPENED=LOPEN)
      IF(.NOT.LOPEN) RETURN
      CALL TRACE_CLEAN(SCOPE, CSCOPE)
      LS = TRACE_LENTRIM(CSCOPE)
      IF(LS.LE.0) LS = 1
C
      SEQ = TRACE_NEXTSEQ()
      WRITE(51,1000) SEQ, CSCOPE(1:LS),
     &               X1, X2, DX,
     &               HK1VAL, T1VAL, RT1VAL, A1VAL,
     &               HK2VAL, T2VAL, RT2VAL, A2INVAL, ACRITVAL,
     &               IDAMPVAL, AX0VAL, A2SEEDVAL
 1000 FORMAT('{"sequence":',I12,',"runtime":"fortran",'
     & '"kind":"transition_point_seed","scope":"',A,
     & '","name":null,"data":{"x1":',1PE24.16,
     & ',"x2":',1PE24.16,',"dx":',1PE24.16,
     & ',"hk1":',1PE24.16,',"t1":',1PE24.16,
     & ',"rt1":',1PE24.16,',"a1":',1PE24.16,
     & ',"hk2":',1PE24.16,',"t2":',1PE24.16,
     & ',"rt2":',1PE24.16,',"a2Input":',1PE24.16,
     & ',"acrit":',1PE24.16,',"idampv":',I2,
     & ',"ax0":',1PE24.16,',"seedAmpl2":',1PE24.16,
     & '},"values":null,"tags":null,"timestampUtc":null}')
      RETURN
      END


      SUBROUTINE TRACE_TRANSITION_POINT_ITER(SCOPE, ITER, X1, X2,
     & AMPL1, AMPL2, AMCRIT, AX, WF2, XT, TT, DT, UT,
     & RESIDUAL, DRESIDUAL, DELTAA2, RLX)
      CHARACTER*(*) SCOPE
      INTEGER TRACE_LENTRIM, TRACE_NEXTSEQ
      INTEGER LS, SEQ, ITER
      REAL X1, X2, AMPL1, AMPL2, AMCRIT, AX, WF2, XT, TT, DT, UT
      REAL RESIDUAL, DRESIDUAL, DELTAA2, RLX
      LOGICAL LOPEN
      CHARACTER*128 CSCOPE
C
      INQUIRE(UNIT=51, OPENED=LOPEN)
      IF(.NOT.LOPEN) RETURN
      CALL TRACE_CLEAN(SCOPE, CSCOPE)
      LS = TRACE_LENTRIM(CSCOPE)
      IF(LS.LE.0) LS = 1
C
      SEQ = TRACE_NEXTSEQ()
      WRITE(51,1000) SEQ, CSCOPE(1:LS), ITER, X1, X2, AMPL1, AMPL2,
     &               AMCRIT, AX, WF2, XT, TT, DT, UT,
     &               RESIDUAL, DRESIDUAL, DELTAA2, RLX
 1000 FORMAT('{"sequence":',I12,',"runtime":"fortran",'
     & '"kind":"transition_point_iteration","scope":"',A,
     & '","name":null,"data":{"iteration":',I6,
     & ',"x1":',1PE24.16,',"x2":',1PE24.16,
     & ',"ampl1":',1PE24.16,',"ampl2":',1PE24.16,
     & ',"amcrit":',1PE24.16,',"ax":',1PE24.16,
     & ',"wf2":',1PE24.16,',"xt":',1PE24.16,
     & ',"tt":',1PE24.16,',"dt":',1PE24.16,
     & ',"ut":',1PE24.16,',"residual":',1PE24.16,
     & ',"residual_A2":',1PE24.16,',"deltaA2":',1PE24.16,
     & ',"relaxation":',1PE24.16,
     & '},"values":null,"tags":null,"timestampUtc":null}')
      RETURN
      END


      SUBROUTINE TRACE_TRANSITION_SEED_SYSTEM(SCOPE, IS, IBL, ITER,
     & MODE, XT, UEI, THI, DSI, AMPL, CTAU, HK2, HK2T2, HK2D2,
     & HK2U2, HTARG, REZ1, REZ2, REZ3,
     & R11, R12, R13, R14, R21, R22, R23, R24,
     & R31, R32, R33, R34)
      CHARACTER*(*) SCOPE, MODE
      INTEGER TRACE_LENTRIM, TRACE_NEXTSEQ
      INTEGER LS, LM, SEQ, IS, IBL, ITER
      REAL XT, UEI, THI, DSI, AMPL, CTAU, HK2, HK2T2, HK2D2, HK2U2
      REAL HTARG, REZ1, REZ2, REZ3
      REAL R11, R12, R13, R14, R21, R22, R23, R24
      REAL R31, R32, R33, R34
      LOGICAL LOPEN
      CHARACTER*128 CSCOPE
      CHARACTER*32 CMODE
C
      INQUIRE(UNIT=51, OPENED=LOPEN)
      IF(.NOT.LOPEN) RETURN
      CALL TRACE_CLEAN(SCOPE, CSCOPE)
      CALL TRACE_CLEAN(MODE, CMODE)
      LS = TRACE_LENTRIM(CSCOPE)
      LM = TRACE_LENTRIM(CMODE)
      IF(LS.LE.0) LS = 1
      IF(LM.LE.0) LM = 1
C
      SEQ = TRACE_NEXTSEQ()
      WRITE(51,1000) SEQ, CSCOPE(1:LS), IS, IBL, ITER, CMODE(1:LM),
     & XT, UEI, THI, DSI, AMPL, CTAU, HK2, HK2T2, HK2D2, HK2U2,
     & HTARG, REZ1, REZ2, REZ3,
     & R11, R12, R13, R14, R21, R22, R23, R24,
     & R31, R32, R33, R34
 1000 FORMAT('{"sequence":',I12,',"runtime":"fortran",'
     & '"kind":"transition_seed_system","scope":"',A,
     & '","name":null,"data":{"side":',I4,
     & ',"station":',I6,',"iteration":',I6,
     & ',"mode":"',A,'","xt":',1PE24.16,
     & ',"uei":',1PE24.16,',"theta":',1PE24.16,
     & ',"dstar":',1PE24.16,',"ampl":',1PE24.16,
     & ',"ctau":',1PE24.16,',"hk2":',1PE24.16,
     & ',"hk2_T2":',1PE24.16,',"hk2_D2":',1PE24.16,
     & ',"hk2_U2":',1PE24.16,',"htarg":',1PE24.16,
     & ',"residual1":',1PE24.16,',"residual2":',1PE24.16,
     & ',"residual3":',1PE24.16,',"row11":',1PE24.16,
     & ',"row12":',1PE24.16,',"row13":',1PE24.16,
     & ',"row14":',1PE24.16,',"row21":',1PE24.16,
     & ',"row22":',1PE24.16,',"row23":',1PE24.16,
     & ',"row24":',1PE24.16,',"row31":',1PE24.16,
     & ',"row32":',1PE24.16,',"row33":',1PE24.16,
     & ',"row34":',1PE24.16,
     & '},"values":null,"tags":null,"timestampUtc":null}')
      RETURN
      END


      SUBROUTINE TRACE_TRANSITION_FINAL_SENSITIVITIES(SCOPE,
     & X1, X2, T1, T2, D1, D2, U1, U2,
     & XT_A2, AMPLT_A2, WF2_A2, TT_A2, DT_A2, UT_A2,
     & TTCOMBO, DTCOMBO, UTCOMBO, AX_AT,
     & AXA1BAS, AXA1TTM, AXA1DTM, AXA1UTM,
     & AXA2ATM, AXA2TTM, AXA2DTM, AXA2UTM,
     & AXT1HKTM, AXT1BASM, AXT1RTTM, AXT1TTTM,
     & AX_T1, AXD1HKTM, AXD1DTTM, AX_D1, AX_U1, AX_A1, AX_X1,
     & AX_T2, AX_D2, AX_U2, AX_A2, AX_X2,
     & Z_A1, Z_T1, Z_D1, Z_U1, Z_X1,
     & Z_A2, Z_T2, Z_D2, Z_U2, Z_X2,
     & WF2A1, XTA1BAS1, XTA1BAS2, XTA1BAS, XTA1COR,
     & XTX2BAS, XTX2COR,
     & XT_A1, XT_T1, XT_D1, XT_U1, XT_X1,
     & XT_T2, XT_D2, XT_U2, XT_X2)
      CHARACTER*(*) SCOPE
      INTEGER TRACE_LENTRIM, TRACE_NEXTSEQ
      INTEGER LS, SEQ
      REAL X1, X2, T1, T2, D1, D2, U1, U2
      REAL XT_A2, AMPLT_A2, WF2_A2, TT_A2, DT_A2, UT_A2
      REAL TTCOMBO, DTCOMBO, UTCOMBO, AX_AT
      REAL AXA1BAS, AXA1TTM, AXA1DTM, AXA1UTM
      REAL AXA2ATM, AXA2TTM, AXA2DTM, AXA2UTM
      REAL AXT1HKTM, AXT1BASM, AXT1RTTM, AXT1TTTM
      REAL AX_T1, AXD1HKTM, AXD1DTTM, AX_D1, AX_U1, AX_A1, AX_X1
      REAL AX_T2, AX_D2, AX_U2, AX_A2, AX_X2
      REAL Z_A1, Z_T1, Z_D1, Z_U1, Z_X1
      REAL Z_A2, Z_T2, Z_D2, Z_U2, Z_X2
      REAL WF2A1, XTA1BAS1, XTA1BAS2, XTA1BAS, XTA1COR
      REAL XTX2BAS, XTX2COR
      REAL XT_A1, XT_T1, XT_D1, XT_U1, XT_X1
      REAL XT_T2, XT_D2, XT_U2, XT_X2
      LOGICAL LOPEN
      CHARACTER*128 CSCOPE
C
      INQUIRE(UNIT=51, OPENED=LOPEN)
      IF(.NOT.LOPEN) RETURN
      CALL TRACE_CLEAN(SCOPE, CSCOPE)
      LS = TRACE_LENTRIM(CSCOPE)
      IF(LS.LE.0) LS = 1
C
      SEQ = TRACE_NEXTSEQ()
      WRITE(51,1000) SEQ, CSCOPE(1:LS),
     & X1, X2, T1, T2, D1, D2, U1, U2,
     & XT_A2, AMPLT_A2, WF2_A2, TT_A2, DT_A2, UT_A2,
     & TTCOMBO, DTCOMBO, UTCOMBO, AX_AT,
     & AXA1BAS, AXA1TTM, AXA1DTM, AXA1UTM,
     & AXA2ATM, AXA2TTM, AXA2DTM, AXA2UTM,
     & AXT1HKTM, AXT1BASM, AXT1RTTM, AXT1TTTM,
     & AX_T1, AXD1HKTM, AXD1DTTM, AX_D1, AX_U1, AX_A1, AX_X1,
     & AX_T2, AX_D2, AX_U2, AX_A2, AX_X2,
     & Z_A1, Z_T1, Z_D1, Z_U1, Z_X1,
     & Z_A2, Z_T2, Z_D2, Z_U2, Z_X2,
     & WF2A1, XTA1BAS1, XTA1BAS2, XTA1BAS, XTA1COR,
     & XTX2BAS, XTX2COR,
     & XT_A1, XT_T1, XT_D1, XT_U1, XT_X1,
     & XT_T2, XT_D2, XT_U2, XT_X2
 1000 FORMAT('{"sequence":',I12,',"runtime":"fortran",'
     & '"kind":"transition_final_sensitivities","scope":"',A,
     & '","name":null,"data":{"x1":',1PE24.16,
     & ',"x2":',1PE24.16,',"t1":',1PE24.16,
     & ',"t2":',1PE24.16,',"d1":',1PE24.16,
     & ',"d2":',1PE24.16,',"u1":',1PE24.16,
     & ',"u2":',1PE24.16,',"xtA2":',1PE24.16,
     & ',"ampltA2":',1PE24.16,',"wf2A2":',1PE24.16,
     & ',"ttA2":',1PE24.16,',"dtA2":',1PE24.16,
     & ',"utA2":',1PE24.16,',"ttCombo":',1PE24.16,
     & ',"dtCombo":',1PE24.16,',"utCombo":',1PE24.16,
     & ',"axAt":',1PE24.16,',"axA1Base":',1PE24.16,
     & ',"axA1TTerm":',1PE24.16,',"axA1DTerm":',1PE24.16,
     & ',"axA1UTerm":',1PE24.16,',"axA2AmplTerm":',1PE24.16,
     & ',"axA2TTerm":',1PE24.16,',"axA2DTerm":',1PE24.16,
     & ',"axA2UTerm":',1PE24.16,
     & ',"axT1HkTerm":',1PE24.16,
     & ',"axT1BaseTerm":',1PE24.16,
     & ',"axT1RtTerm":',1PE24.16,
     & ',"axT1TtTerm":',1PE24.16,
     & ',"axT1":',1PE24.16,
     & ',"axD1HkTerm":',1PE24.16,
     & ',"axD1DtTerm":',1PE24.16,
     & ',"axD1":',1PE24.16,
     & ',"axU1":',1PE24.16,',"axA1":',1PE24.16,
     & ',"axX1":',1PE24.16,',"axT2":',1PE24.16,
     & ',"axD2":',1PE24.16,',"axU2":',1PE24.16,
     & ',"axA2":',1PE24.16,',"axX2":',1PE24.16,
     & ',"zA1":',1PE24.16,',"zT1":',1PE24.16,
     & ',"zD1":',1PE24.16,',"zU1":',1PE24.16,
     & ',"zX1":',1PE24.16,',"zA2":',1PE24.16,
     & ',"zT2":',1PE24.16,',"zD2":',1PE24.16,
     & ',"zU2":',1PE24.16,',"zX2":',1PE24.16,
     & ',"wf2A1":',1PE24.16,
     & ',"xtA1BaseTerm1":',1PE24.16,
     & ',"xtA1BaseTerm2":',1PE24.16,
     & ',"xtA1Base":',1PE24.16,
     & ',"xtA1Correction":',1PE24.16,
     & ',"xtX2Base":',1PE24.16,
     & ',"xtX2Correction":',1PE24.16,
     & ',"xtA1":',1PE24.16,',"xtT1":',1PE24.16,
     & ',"xtD1":',1PE24.16,',"xtU1":',1PE24.16,
     & ',"xtX1":',1PE24.16,',"xtT2":',1PE24.16,
     & ',"xtD2":',1PE24.16,',"xtU2":',1PE24.16,
     & ',"xtX2":',1PE24.16,
     & '},"values":null,"tags":null,"timestampUtc":null}')
      RETURN
      END

      SUBROUTINE TRACE_TRANSITION_INTERVAL_TERM_COMPONENTS(SCOPE, WF2,
     & WF2_XT, WF1_A1, WF1_T1, WF1_T2, WF2_A1, WF2_T1, WF2_T2,
     & WF2_X1_TERM1, WF2_X1_TERM2, WF2_X2_TERM1, WF2_X2_TERM2,
     & TT_A1_TERM1, TT_A1_TERM2, DT_A1_TERM1, DT_A1_TERM2,
     & DT_T1_TERM1, DT_T1_TERM2, DT_T2_TERM1, DT_T2_TERM2,
     & UT_A1_TERM1, UT_A1_TERM2, UT_T1_TERM1, UT_T1_TERM2,
     & UT_T2_TERM1, UT_T2_TERM2)
      CHARACTER*(*) SCOPE
      INTEGER TRACE_LENTRIM, TRACE_NEXTSEQ
      INTEGER LS, SEQ
      REAL WF2, WF2_XT, WF1_A1, WF1_T1, WF1_T2, WF2_A1, WF2_T1, WF2_T2
      REAL WF2_X1_TERM1, WF2_X1_TERM2, WF2_X2_TERM1, WF2_X2_TERM2
      REAL TT_A1_TERM1, TT_A1_TERM2, DT_A1_TERM1, DT_A1_TERM2
      REAL DT_T1_TERM1, DT_T1_TERM2, DT_T2_TERM1, DT_T2_TERM2
      REAL UT_A1_TERM1, UT_A1_TERM2, UT_T1_TERM1, UT_T1_TERM2
      REAL UT_T2_TERM1, UT_T2_TERM2
      LOGICAL LOPEN
      CHARACTER*128 CSCOPE
C
      INQUIRE(UNIT=51, OPENED=LOPEN)
      IF(.NOT.LOPEN) RETURN
      CALL TRACE_CLEAN(SCOPE, CSCOPE)
      LS = TRACE_LENTRIM(CSCOPE)
      IF(LS.LE.0) LS = 1
C
      SEQ = TRACE_NEXTSEQ()
      WRITE(51,1000) SEQ, CSCOPE(1:LS),
     & WF2, WF2_XT, WF1_A1, WF1_T1, WF1_T2, WF2_A1, WF2_T1, WF2_T2,
     & WF2_X1_TERM1, WF2_X1_TERM2, WF2_X2_TERM1, WF2_X2_TERM2,
     & TT_A1_TERM1, TT_A1_TERM2, DT_A1_TERM1, DT_A1_TERM2,
     & DT_T1_TERM1, DT_T1_TERM2, DT_T2_TERM1, DT_T2_TERM2,
     & UT_A1_TERM1, UT_A1_TERM2, UT_T1_TERM1, UT_T1_TERM2,
     & UT_T2_TERM1, UT_T2_TERM2
 1000 FORMAT('{"sequence":',I12,',"runtime":"fortran",'
     & '"kind":"transition_interval_term_components","scope":"',A,
     & '","name":null,"data":{"wf2":',1PE24.16,
     & ',"wf2Xt":',1PE24.16,
     & ',"wf1A1":',1PE24.16,
     & ',"wf1T1":',1PE24.16,
     & ',"wf1T2":',1PE24.16,
     & ',"wf2A1":',1PE24.16,
     & ',"wf2T1":',1PE24.16,
     & ',"wf2T2":',1PE24.16,
     & ',"wf2X1Term1":',1PE24.16,
     & ',"wf2X1Term2":',1PE24.16,
     & ',"wf2X2Term1":',1PE24.16,
     & ',"wf2X2Term2":',1PE24.16,
     & ',"ttA1Term1":',1PE24.16,
     & ',"ttA1Term2":',1PE24.16,
     & ',"dtA1Term1":',1PE24.16,
     & ',"dtA1Term2":',1PE24.16,
     & ',"dtT1Term1":',1PE24.16,
     & ',"dtT1Term2":',1PE24.16,
     & ',"dtT2Term1":',1PE24.16,
     & ',"dtT2Term2":',1PE24.16,
     & ',"utA1Term1":',1PE24.16,
     & ',"utA1Term2":',1PE24.16,
     & ',"utT1Term1":',1PE24.16,
     & ',"utT1Term2":',1PE24.16,
     & ',"utT2Term1":',1PE24.16,
     & ',"utT2Term2":',1PE24.16,
     & '},"values":null,"tags":null,"timestampUtc":null}')
      RETURN
      END


      SUBROUTINE TRACE_TRANSITION_INTERVAL_INPUTS(SCOPE, X1, X2, XT,
     & X1ORIG, T1ORIG, D1ORIG, S2ORIG, U1ORIG, T1, T2, D1, D2, U1, U2,
     & XT_A1, XT_T1, XT_T2, XT_D1, XT_D2,
     & XT_U1, XT_U2, XT_X1, XT_X2,
     & WF2_A1, WF2_T1, WF2_T2, WF2_D1, WF2_D2,
     & WF2_U1, WF2_U2, WF2_X1, WF2_X2,
     & TT_A1, TT_T1, TT_T2, TT_D1, TT_D2,
     & DT_A1, DT_T1, DT_T2, DT_D1, DT_D2,
     & UT_A1, UT_T1, UT_T2, UT_D1, UT_D2, UT_U1, UT_U2,
     & ST, ST_A1, ST_T1, ST_T2, ST_D1, ST_D2,
     & ST_U1, ST_U2, ST_X1, ST_X2)
      CHARACTER*(*) SCOPE
      INTEGER TRACE_LENTRIM, TRACE_NEXTSEQ
      INTEGER LS, SEQ
      REAL X1, X2, XT
      REAL X1ORIG, T1ORIG, D1ORIG, S2ORIG, U1ORIG
      REAL T1, T2, D1, D2, U1, U2
      REAL XT_A1, XT_T1, XT_T2, XT_D1, XT_D2
      REAL XT_U1, XT_U2, XT_X1, XT_X2
      REAL WF2_A1, WF2_T1, WF2_T2, WF2_D1, WF2_D2
      REAL WF2_U1, WF2_U2, WF2_X1, WF2_X2
      REAL TT_A1, TT_T1, TT_T2, TT_D1, TT_D2
      REAL DT_A1, DT_T1, DT_T2, DT_D1, DT_D2
      REAL UT_A1, UT_T1, UT_T2, UT_D1, UT_D2, UT_U1, UT_U2
      REAL ST, ST_A1, ST_T1, ST_T2, ST_D1, ST_D2
      REAL ST_U1, ST_U2, ST_X1, ST_X2
      LOGICAL LOPEN
      CHARACTER*128 CSCOPE
C
      INQUIRE(UNIT=51, OPENED=LOPEN)
      IF(.NOT.LOPEN) RETURN
      CALL TRACE_CLEAN(SCOPE, CSCOPE)
      LS = TRACE_LENTRIM(CSCOPE)
      IF(LS.LE.0) LS = 1
C
      SEQ = TRACE_NEXTSEQ()
      WRITE(51,1000) SEQ, CSCOPE(1:LS),
     & X1, X2, XT,
     & X1ORIG, T1ORIG, D1ORIG, S2ORIG, U1ORIG,
     & T1, T2, D1, D2, U1, U2,
     & XT_A1, XT_T1, XT_T2, XT_D1, XT_D2,
     & XT_U1, XT_U2, XT_X1, XT_X2,
     & WF2_A1, WF2_T1, WF2_T2, WF2_D1, WF2_D2,
     & WF2_U1, WF2_U2, WF2_X1, WF2_X2,
     & TT_A1, TT_T1, TT_T2, TT_D1, TT_D2,
     & DT_A1, DT_T1, DT_T2, DT_D1, DT_D2,
     & UT_A1, UT_T1, UT_T2, UT_D1, UT_D2, UT_U1, UT_U2,
     & ST, ST_A1, ST_T1, ST_T2, ST_D1, ST_D2,
     & ST_U1, ST_U2, ST_X1, ST_X2
 1000 FORMAT('{"sequence":',I12,',"runtime":"fortran",'
     & '"kind":"transition_interval_inputs","scope":"',A,
     & '","name":null,"data":{"x1":',1PE24.16,
     & ',"x2":',1PE24.16,',"xt":',1PE24.16,
     & ',"x1Original":',1PE24.16,
     & ',"t1Original":',1PE24.16,
     & ',"d1Original":',1PE24.16,
     & ',"s2Original":',1PE24.16,
     & ',"u1Original":',1PE24.16,
     & ',"t1":',1PE24.16,',"t2":',1PE24.16,
     & ',"d1":',1PE24.16,',"d2":',1PE24.16,
     & ',"u1":',1PE24.16,',"u2":',1PE24.16,
     & ',"xtA1":',1PE24.16,',"xtT1":',1PE24.16,
     & ',"xtT2":',1PE24.16,
     & ',"xtD1":',1PE24.16,',"xtD2":',1PE24.16,
     & ',"xtU1":',1PE24.16,',"xtU2":',1PE24.16,
     & ',"xtX1":',1PE24.16,',"xtX2":',1PE24.16,
     & ',"wf2A1":',1PE24.16,',"wf2T1":',1PE24.16,
     & ',"wf2T2":',1PE24.16,',"wf2D1":',1PE24.16,
     & ',"wf2D2":',1PE24.16,',"wf2U1":',1PE24.16,
     & ',"wf2U2":',1PE24.16,',"wf2X1":',1PE24.16,
     & ',"wf2X2":',1PE24.16,',"ttA1":',1PE24.16,
     & ',"ttT1":',1PE24.16,',"ttT2":',1PE24.16,
     & ',"ttD1":',1PE24.16,',"ttD2":',1PE24.16,
     & ',"dtA1":',1PE24.16,',"dtT1":',1PE24.16,
     & ',"dtT2":',1PE24.16,',"dtD1":',1PE24.16,
     & ',"dtD2":',1PE24.16,',"utA1":',1PE24.16,
     & ',"utT1":',1PE24.16,',"utT2":',1PE24.16,
     & ',"utD1":',1PE24.16,',"utD2":',1PE24.16,
     & ',"utU1":',1PE24.16,',"utU2":',1PE24.16,
     & ',"st":',1PE24.16,',"stA1":',1PE24.16,
     & ',"stT1":',1PE24.16,',"stT2":',1PE24.16,
     & ',"stD1":',1PE24.16,',"stD2":',1PE24.16,
     & ',"stU1":',1PE24.16,',"stU2":',1PE24.16,
     & ',"stX1":',1PE24.16,',"stX2":',1PE24.16,
     & '},"values":null,"tags":null,"timestampUtc":null}')
      RETURN
      END


      SUBROUTINE TRACE_TRANSITION_INTERVAL_US2_TERMS(SCOPE,
     & US2, US2_HS2, US2_HK2, US2_H2,
     & US2_THS2, US2_THK2, US2_TH2,
     & US2_DHS2, US2_DHK2, US2_DH2,
     & US2_T2, US2_D2)
      CHARACTER*(*) SCOPE
      INTEGER TRACE_LENTRIM, TRACE_NEXTSEQ
      INTEGER LS, SEQ
      REAL US2, US2_HS2, US2_HK2, US2_H2
      REAL US2_THS2, US2_THK2, US2_TH2
      REAL US2_DHS2, US2_DHK2, US2_DH2
      REAL US2_T2, US2_D2
      LOGICAL LOPEN
      CHARACTER*128 CSCOPE
C
      INQUIRE(UNIT=51, OPENED=LOPEN)
      IF(.NOT.LOPEN) RETURN
      CALL TRACE_CLEAN(SCOPE, CSCOPE)
      LS = TRACE_LENTRIM(CSCOPE)
      IF(LS.LE.0) LS = 1
C
      SEQ = TRACE_NEXTSEQ()
      WRITE(51,1000) SEQ, CSCOPE(1:LS),
     & US2, US2_HS2, US2_HK2, US2_H2,
     & US2_THS2, US2_THK2, US2_TH2,
     & US2_DHS2, US2_DHK2, US2_DH2,
     & US2_T2, US2_D2
 1000 FORMAT('{"sequence":',I12,',"runtime":"fortran",'
     & '"kind":"transition_interval_us2_terms","scope":"',A,
     & '","name":null,"data":{"usT":',1PE24.16,
     & ',"usTHs":',1PE24.16,',"usTHk":',1PE24.16,
     & ',"usTH":',1PE24.16,',"usTTermHs":',1PE24.16,
     & ',"usTTermHk":',1PE24.16,',"usTTermH":',1PE24.16,
     & ',"usDTermHs":',1PE24.16,',"usDTermHk":',1PE24.16,
     & ',"usDTermH":',1PE24.16,',"usTTt":',1PE24.16,
     & ',"usTDt":',1PE24.16,
     & '},"values":null,"tags":null,"timestampUtc":null}')
      RETURN
      END


      SUBROUTINE TRACE_TRANSITION_INTERVAL_ST_TERMS(SCOPE,
     & HS2, HS2_HK2, HS2_RT2, HS2_T2, HS2_D2, HS2_U2,
     & US2, US2_HS2, US2_HK2, US2_H2, US2_T2, US2_D2, US2_U2,
     & US2_THS2, US2_THK2, US2_TH2,
     & US2_DHS2, US2_DHK2, US2_DH2,
     & H2, H2_T2, H2_D2,
     & HK2, RT2, RT2_T2, RT2_U2, M2,
     & CTR, CTR_HK2,
     & CQ2, CQ2_T2, CQ2_D2, CQ2_U2,
     & HK2_T2, HK2_D2, HK2_U2,
     & ST_TT, ST_DT, ST_UT,
     & TT_A1, TT_T1, TT_T2,
     & DT_A1, DT_T1, DT_T2,
     & UT_A1, UT_T1, UT_T2,
     & ST_A1, ST_T1, ST_T2,
     & TT_U1, TT_U2, DT_U1, DT_U2, UT_U1, UT_U2,
     & TT_X1, TT_X2, DT_X1, DT_X2, UT_X1, UT_X2,
     & ST_U1, ST_U2, ST_X1, ST_X2)
      CHARACTER*(*) SCOPE
      INTEGER TRACE_LENTRIM, TRACE_NEXTSEQ
      INTEGER LS, SEQ
      REAL HS2, HS2_HK2, HS2_RT2, HS2_T2, HS2_D2, HS2_U2
      REAL US2, US2_HS2, US2_HK2, US2_H2, US2_T2, US2_D2, US2_U2
      REAL US2_THS2, US2_THK2, US2_TH2
      REAL US2_DHS2, US2_DHK2, US2_DH2
      REAL H2, H2_T2, H2_D2
      REAL HK2, RT2, RT2_T2, RT2_U2, M2
      REAL CTR, CTR_HK2
      REAL CQ2, CQ2_T2, CQ2_D2, CQ2_U2
      REAL HK2_T2, HK2_D2, HK2_U2
      REAL ST_TT, ST_DT, ST_UT
      REAL TT_A1, TT_T1, TT_T2
      REAL DT_A1, DT_T1, DT_T2
      REAL UT_A1, UT_T1, UT_T2
      REAL ST_A1, ST_T1, ST_T2
      REAL TT_U1, TT_U2, DT_U1, DT_U2, UT_U1, UT_U2
      REAL TT_X1, TT_X2, DT_X1, DT_X2, UT_X1, UT_X2
      REAL ST_U1, ST_U2, ST_X1, ST_X2
      LOGICAL LOPEN
      CHARACTER*128 CSCOPE
C
      INQUIRE(UNIT=51, OPENED=LOPEN)
      IF(.NOT.LOPEN) RETURN
      CALL TRACE_CLEAN(SCOPE, CSCOPE)
      LS = TRACE_LENTRIM(CSCOPE)
      IF(LS.LE.0) LS = 1
C
      SEQ = TRACE_NEXTSEQ()
      WRITE(51,1000) SEQ, CSCOPE(1:LS),
     & HS2, HS2_HK2, HS2_RT2, HS2_T2, HS2_D2, HS2_U2,
     & US2, US2_HS2, US2_HK2, US2_H2, US2_T2, US2_D2, US2_U2,
     & US2_THS2, US2_THK2, US2_TH2,
     & US2_DHS2, US2_DHK2, US2_DH2,
     & H2, H2_T2, H2_D2,
     & HK2, RT2, RT2_T2, RT2_U2, M2,
     & CTR, CTR_HK2,
     & CQ2, CQ2_T2, CQ2_D2, CQ2_U2,
     & HK2_T2, HK2_D2, HK2_U2,
     & ST_TT, ST_DT, ST_UT,
     & TT_A1, TT_T1, TT_T2,
     & DT_A1, DT_T1, DT_T2,
     & UT_A1, UT_T1, UT_T2,
     & ST_A1, ST_T1, ST_T2,
     & TT_U1, TT_U2, DT_U1, DT_U2, UT_U1, UT_U2,
     & TT_X1, TT_X2, DT_X1, DT_X2, UT_X1, UT_X2,
     & ST_U1, ST_U2, ST_X1, ST_X2
 1000 FORMAT('{"sequence":',I12,',"runtime":"fortran",'
     & '"kind":"transition_interval_st_terms","scope":"',A,
     & '","name":null,"data":{"hsT":',1PE24.16,
     & ',"hsTHk":',1PE24.16,',"hsTRt":',1PE24.16,
     & ',"hsTTt":',1PE24.16,',"hsTDt":',1PE24.16,
     & ',"hsTUt":',1PE24.16,',"usT":',1PE24.16,
     & ',"usTHs":',1PE24.16,',"usTHk":',1PE24.16,
     & ',"usTH":',1PE24.16,',"usTTt":',1PE24.16,
     & ',"usTDt":',1PE24.16,',"usTUt":',1PE24.16,
     & ',"usTTermHs":',1PE24.16,',"usTTermHk":',1PE24.16,
     & ',"usTTermH":',1PE24.16,',"usDTermHs":',1PE24.16,
     & ',"usDTermHk":',1PE24.16,',"usDTermH":',1PE24.16,
     & ',"hT":',1PE24.16,
     & ',"hTTt":',1PE24.16,',"hTDt":',1PE24.16,
     & ',"hk2":',1PE24.16,',"rtT":',1PE24.16,
     & ',"rtTTt":',1PE24.16,',"rtTUt":',1PE24.16,
     & ',"m2":',1PE24.16,',"ctr":',1PE24.16,
     & ',"ctrHk2":',1PE24.16,',"cqT":',1PE24.16,
     & ',"cqTTt":',1PE24.16,',"cqTDt":',1PE24.16,
     & ',"cqTUt":',1PE24.16,',"hk2Tt":',1PE24.16,
     & ',"hk2Dt":',1PE24.16,',"hk2Ut":',1PE24.16,
     & ',"stTt":',1PE24.16,',"stDt":',1PE24.16,
     & ',"stUt":',1PE24.16,',"ttA1":',1PE24.16,
     & ',"ttT1":',1PE24.16,',"ttT2":',1PE24.16,
     & ',"dtA1":',1PE24.16,',"dtT1":',1PE24.16,
     & ',"dtT2":',1PE24.16,',"utA1":',1PE24.16,
     & ',"utT1":',1PE24.16,',"utT2":',1PE24.16,
     & ',"stA1":',1PE24.16,',"stT1":',1PE24.16,
     & ',"stT2":',1PE24.16,
     & ',"ttU1":',1PE24.16,',"ttU2":',1PE24.16,
     & ',"dtU1":',1PE24.16,',"dtU2":',1PE24.16,
     & ',"utU1":',1PE24.16,',"utU2":',1PE24.16,
     & ',"ttX1":',1PE24.16,',"ttX2":',1PE24.16,
     & ',"dtX1":',1PE24.16,',"dtX2":',1PE24.16,
     & ',"utX1":',1PE24.16,',"utX2":',1PE24.16,
     & ',"stU1":',1PE24.16,',"stU2":',1PE24.16,
     & ',"stX1":',1PE24.16,',"stX2":',1PE24.16,
     & '},"values":null,"tags":null,"timestampUtc":null}')
      RETURN
      END


      SUBROUTINE TRACE_TRANSITION_INTERVAL_BT2_TERMS(SCOPE,
     & SIDE, STATION, IROW, JCOL,
     & BASEVS2, STTERM, TTTERM, DTTERM, UTTERM, XTTERM, FINALVALUE)
      CHARACTER*(*) SCOPE
      INTEGER TRACE_LENTRIM, TRACE_NEXTSEQ
      INTEGER LS, SEQ, SIDE, STATION, IROW, JCOL
      INTEGER BASEBITS, STBITS, TTBITS, DTBITS
      INTEGER UTBITS, XTBITS, FINALBITS
      REAL BASEVS2, STTERM, TTTERM, DTTERM, UTTERM, XTTERM, FINALVALUE
      REAL BASETMP, STTMP, TTTMP, DTTMP, UTTMP, XTTMP, FINALTMP
      LOGICAL LOPEN
      CHARACTER*128 CSCOPE
C
      EQUIVALENCE (BASETMP, BASEBITS), (STTMP, STBITS)
      EQUIVALENCE (TTTMP, TTBITS), (DTTMP, DTBITS)
      EQUIVALENCE (UTTMP, UTBITS), (XTTMP, XTBITS)
      EQUIVALENCE (FINALTMP, FINALBITS)
C
      INQUIRE(UNIT=51, OPENED=LOPEN)
      IF(.NOT.LOPEN) RETURN
      CALL TRACE_CLEAN(SCOPE, CSCOPE)
      LS = TRACE_LENTRIM(CSCOPE)
      IF(LS.LE.0) LS = 1
C
      BASETMP = BASEVS2
      STTMP = STTERM
      TTTMP = TTTERM
      DTTMP = DTTERM
      UTTMP = UTTERM
      XTTMP = XTTERM
      FINALTMP = FINALVALUE
      SEQ = TRACE_NEXTSEQ()
      WRITE(51,1000) SEQ, CSCOPE(1:LS), SIDE, STATION, IROW, JCOL,
     & BASEVS2, BASEBITS,
     & STTERM, STBITS, TTTERM, TTBITS,
     & DTTERM, DTBITS, UTTERM, UTBITS,
     & XTTERM, XTBITS, FINALVALUE, FINALBITS
 1000 FORMAT('{"sequence":',I12,',"runtime":"fortran",'
     & '"kind":"transition_interval_bt2_terms","scope":"',A,
     & '","name":null,"data":{"side":',I8,',"station":',I8,
     & ',"row":',I2,',"column":',I2,
     & ',"baseVs2":',1PE24.16,
     & ',"baseBits":',I12,
     & ',"stTerm":',1PE24.16,',"stBits":',I12,
     & ',"ttTerm":',1PE24.16,',"ttBits":',I12,
     & ',"dtTerm":',1PE24.16,',"dtBits":',I12,
     & ',"utTerm":',1PE24.16,',"utBits":',I12,
     & ',"xtTerm":',1PE24.16,',"xtBits":',I12,
     & ',"final":',1PE24.16,',"finalBits":',I12,
     & '},"values":null,"tags":null,"timestampUtc":null}')
      RETURN
      END


      SUBROUTINE TRACE_TRANSITION_INTERVAL_FINAL_TERMS(SCOPE,
     & SIDE, STATION, IROW, JCOL,
     & LAMINARVALUE, TURBULENTVALUE, FINALVALUE)
      CHARACTER*(*) SCOPE
      INTEGER TRACE_LENTRIM, TRACE_NEXTSEQ
      INTEGER LS, SEQ, SIDE, STATION, IROW, JCOL
      INTEGER LAMINARBITS, TURBULENTBITS, FINALBITS
      REAL LAMINARVALUE, TURBULENTVALUE, FINALVALUE
      REAL LAMINARTMP, TURBULENTTMP, FINALTMP
      LOGICAL LOPEN
      CHARACTER*128 CSCOPE
C
      EQUIVALENCE (LAMINARTMP, LAMINARBITS)
      EQUIVALENCE (TURBULENTTMP, TURBULENTBITS)
      EQUIVALENCE (FINALTMP, FINALBITS)
C
      INQUIRE(UNIT=51, OPENED=LOPEN)
      IF(.NOT.LOPEN) RETURN
      CALL TRACE_CLEAN(SCOPE, CSCOPE)
      LS = TRACE_LENTRIM(CSCOPE)
      IF(LS.LE.0) LS = 1
C
      LAMINARTMP = LAMINARVALUE
      TURBULENTTMP = TURBULENTVALUE
      FINALTMP = FINALVALUE
      SEQ = TRACE_NEXTSEQ()
      WRITE(51,1000) SEQ, CSCOPE(1:LS), SIDE, STATION, IROW, JCOL,
     & LAMINARVALUE, LAMINARBITS,
     & TURBULENTVALUE, TURBULENTBITS,
     & FINALVALUE, FINALBITS
 1000 FORMAT('{"sequence":',I12,',"runtime":"fortran",'
     & '"kind":"transition_interval_final_terms","scope":"',A,
     & '","name":null,"data":{"side":',I8,',"station":',I8,
     & ',"row":',I2,',"column":',I2,
     & ',"laminarValue":',1PE24.16,',"laminarBits":',I12,
     & ',"turbulentValue":',1PE24.16,',"turbulentBits":',I12,
     & ',"final":',1PE24.16,',"finalBits":',I12,
     & '},"values":null,"tags":null,"timestampUtc":null}')
      RETURN
      END


      SUBROUTINE TRACE_TRANSITION_INTERVAL_BT2_D_TERMS(SCOPE,
     & BASEVS2, STTERM, TTTERM, DTTERM, UTTERM, XTTERM, ROW13)
      CHARACTER*(*) SCOPE
      INTEGER TRACE_LENTRIM, TRACE_NEXTSEQ
      INTEGER LS, SEQ
      REAL BASEVS2, STTERM, TTTERM, DTTERM, UTTERM, XTTERM, ROW13
      LOGICAL LOPEN
      CHARACTER*128 CSCOPE
C
      INQUIRE(UNIT=51, OPENED=LOPEN)
      IF(.NOT.LOPEN) RETURN
      CALL TRACE_CLEAN(SCOPE, CSCOPE)
      LS = TRACE_LENTRIM(CSCOPE)
      IF(LS.LE.0) LS = 1
C
      SEQ = TRACE_NEXTSEQ()
      WRITE(51,1000) SEQ, CSCOPE(1:LS),
     & BASEVS2, STTERM, TTTERM, DTTERM, UTTERM, XTTERM, ROW13
 1000 FORMAT('{"sequence":',I12,',"runtime":"fortran",'
     & '"kind":"transition_interval_bt2_d_terms","scope":"',A,
     & '","name":null,"data":{"baseVs2":',1PE24.16,
     & ',"stTerm":',1PE24.16,',"ttTerm":',1PE24.16,
     & ',"dtTerm":',1PE24.16,',"utTerm":',1PE24.16,
     & ',"xtTerm":',1PE24.16,',"row13":',1PE24.16,
     & '},"values":null,"tags":null,"timestampUtc":null}')
      RETURN
      END


      SUBROUTINE TRACE_TRANSITION_INTERVAL_ROWS(SCOPE, X1, X2, XT,
     & WF1, WF2, TT, DT, UT, ST,
     & BLREZ2, BLREZ3,
     & BL31, BL32, BL33, BL34, BL35,
     & BL41, BL42, BL43, BL44, BL45,
     & BL22, BL14, BL24,
     & BTREZ1, BTREZ2, BTREZ3,
     & BT31, BT32, BT33, BT34, BT35,
     & BT41, BT42, BT43, BT44, BT45,
     & BT22, BT14, BT24,
     & FV22, FV14, FV24,
     & FREZ3, FV31, FV32, FV33, FV34, FV35)
      CHARACTER*(*) SCOPE
      INTEGER TRACE_LENTRIM, TRACE_NEXTSEQ
      INTEGER LS, SEQ
      REAL X1, X2, XT, WF1, WF2, TT, DT, UT, ST
      REAL BLREZ2, BLREZ3
      REAL BL31, BL32, BL33, BL34, BL35
      REAL BL41, BL42, BL43, BL44, BL45
      REAL BL22, BL14, BL24
      REAL BTREZ1, BTREZ2, BTREZ3
      REAL BT31, BT32, BT33, BT34, BT35
      REAL BT41, BT42, BT43, BT44, BT45
      REAL BT22, BT14, BT24
      REAL FV22, FV14, FV24
      REAL FREZ3, FV31, FV32, FV33, FV34, FV35
      LOGICAL LOPEN
      CHARACTER*128 CSCOPE
C
      INQUIRE(UNIT=51, OPENED=LOPEN)
      IF(.NOT.LOPEN) RETURN
      CALL TRACE_CLEAN(SCOPE, CSCOPE)
      LS = TRACE_LENTRIM(CSCOPE)
      IF(LS.LE.0) LS = 1
C
      SEQ = TRACE_NEXTSEQ()
      WRITE(51,1000) SEQ, CSCOPE(1:LS),
     & X1, X2, XT, WF1, WF2, TT, DT, UT, ST,
     & BLREZ2, BLREZ3,
     & BL31, BL32, BL33, BL34, BL35,
     & BL41, BL22, BL42, BL43, BL44, BL45,
     & BL14, BL24,
     & BTREZ1, BTREZ2, BTREZ3,
     & BT31, BT32, BT33, BT34, BT35,
     & BT41, BT22, BT42, BT43, BT44, BT45,
     & BT14, BT24,
     & FV22, FV14, FV24,
     & FREZ3, FV31, FV32, FV33, FV34, FV35
 1000 FORMAT('{"sequence":',I12,',"runtime":"fortran",'
     & '"kind":"transition_interval_rows","scope":"',A,
     & '","name":null,"data":{"x1":',1PE24.16,
     & ',"x2":',1PE24.16,',"xt":',1PE24.16,
     & ',"wf1":',1PE24.16,',"wf2":',1PE24.16,
     & ',"tt":',1PE24.16,',"dt":',1PE24.16,
     & ',"ut":',1PE24.16,',"st":',1PE24.16,
     & ',"laminarResidual2":',1PE24.16,
     & ',"laminarResidual3":',1PE24.16,
     & ',"laminarVs1_31":',1PE24.16,
     & ',"laminarVs1_32":',1PE24.16,
     & ',"laminarVs1_33":',1PE24.16,
     & ',"laminarVs1_34":',1PE24.16,
     & ',"laminarVs1_35":',1PE24.16,
     & ',"laminarVs2_31":',1PE24.16,
     & ',"laminarVs2_22":',1PE24.16,
     & ',"laminarVs2_32":',1PE24.16,
     & ',"laminarVs2_33":',1PE24.16,
     & ',"laminarVs2_34":',1PE24.16,
     & ',"laminarVs2_35":',1PE24.16,
     & ',"laminarVs2_14":',1PE24.16,
     & ',"laminarVs2_24":',1PE24.16,
     & ',"turbulentResidual1":',1PE24.16,
     & ',"turbulentResidual2":',1PE24.16,
     & ',"turbulentResidual3":',1PE24.16,
     & ',"turbulentVs1_31":',1PE24.16,
     & ',"turbulentVs1_32":',1PE24.16,
     & ',"turbulentVs1_33":',1PE24.16,
     & ',"turbulentVs1_34":',1PE24.16,
     & ',"turbulentVs1_35":',1PE24.16,
     & ',"turbulentVs2_31":',1PE24.16,
     & ',"turbulentVs2_22":',1PE24.16,
     & ',"turbulentVs2_32":',1PE24.16,
     & ',"turbulentVs2_33":',1PE24.16,
     & ',"turbulentVs2_34":',1PE24.16,
     & ',"turbulentVs2_35":',1PE24.16,
     & ',"turbulentVs2_14":',1PE24.16,
     & ',"turbulentVs2_24":',1PE24.16,
     & ',"finalVs2_22":',1PE24.16,
     & ',"finalVs2_14":',1PE24.16,
     & ',"finalVs2_24":',1PE24.16,
     & ',"finalResidual3":',1PE24.16,
     & ',"finalVs2_31":',1PE24.16,
     & ',"finalVs2_32":',1PE24.16,
     & ',"finalVs2_33":',1PE24.16,
     & ',"finalVs2_34":',1PE24.16,
     & ',"finalVs2_35":',1PE24.16,
     & '},"values":null,"tags":null,"timestampUtc":null}')
      RETURN
      END


      SUBROUTINE TRACE_PSILIN_FIELD(SCOPE, FIELDINDEX, XVAL, YVAL,
     &                              NXVAL, NYVAL, GEOLIN, SIGLIN)
      CHARACTER*(*) SCOPE
      INTEGER TRACE_LENTRIM, TRACE_NEXTSEQ
      INTEGER LS, SEQ, FIELDINDEX, IGEO, ISIG
      REAL XVAL, YVAL, NXVAL, NYVAL
      LOGICAL GEOLIN, SIGLIN, LOPEN
      CHARACTER*128 CSCOPE
C
      INQUIRE(UNIT=51, OPENED=LOPEN)
      IF(.NOT.LOPEN) RETURN
      CALL TRACE_CLEAN(SCOPE, CSCOPE)
      LS = TRACE_LENTRIM(CSCOPE)
      IF(LS.LE.0) LS = 1
      IGEO = 0
      IF(GEOLIN) IGEO = 1
      ISIG = 0
      IF(SIGLIN) ISIG = 1
C
      SEQ = TRACE_NEXTSEQ()
      WRITE(51,1000) SEQ, CSCOPE(1:LS), FIELDINDEX,
     &               XVAL, YVAL, NXVAL, NYVAL, IGEO, ISIG
 1000 FORMAT('{"sequence":',I12,',"runtime":"fortran",'
     & '"kind":"psilin_field","scope":"',A,
     & '","name":null,"data":{"fieldIndex":',I6,
     & ',"fieldX":',1PE24.16,',"fieldY":',1PE24.16,
     & ',"fieldNormalX":',1PE24.16,',"fieldNormalY":',1PE24.16,
     & ',"computeGeometricSensitivities":',I2,
     & ',"includeSourceTerms":',I2,
     & ',"precision":"Single"},'
     & '"values":null,"tags":null,"timestampUtc":null}')
      RETURN
      END


      SUBROUTINE TRACE_PSILIN_PANEL(SCOPE, FIELDINDEX, PANELINDEX,
     & JM, JO, JP, JQ, GEOLIN, SIGLIN,
     & PANELXJO, PANELYJO, PANELXJP, PANELYJP, PANELDX, PANELDY,
     & DSO, DSIO, APAN,
     & RX1, RY1, RX2, RY2, SX, SY,
     & X1, X2, YY, RS1, RS2, SGN,
     & G1, G2, T1, T2, X1I, X2I, YYI)
      CHARACTER*(*) SCOPE
      INTEGER TRACE_LENTRIM, TRACE_NEXTSEQ
      INTEGER LS, SEQ, FIELDINDEX, PANELINDEX, JM, JO, JP, JQ
      INTEGER IGEO, ISIG
      REAL PANELXJO, PANELYJO, PANELXJP, PANELYJP, PANELDX, PANELDY
      REAL DSO, DSIO, APAN, RX1, RY1, RX2, RY2, SX, SY
      REAL X1, X2, YY, RS1, RS2, SGN, G1, G2, T1, T2, X1I, X2I, YYI
      LOGICAL GEOLIN, SIGLIN, LOPEN
      CHARACTER*128 CSCOPE
C
      INQUIRE(UNIT=51, OPENED=LOPEN)
      IF(.NOT.LOPEN) RETURN
      CALL TRACE_CLEAN(SCOPE, CSCOPE)
      LS = TRACE_LENTRIM(CSCOPE)
      IF(LS.LE.0) LS = 1
      IGEO = 0
      IF(GEOLIN) IGEO = 1
      ISIG = 0
      IF(SIGLIN) ISIG = 1
C
      SEQ = TRACE_NEXTSEQ()
      WRITE(51,1000) SEQ, CSCOPE(1:LS),
     & FIELDINDEX, PANELINDEX, JM, JO, JP, JQ, IGEO, ISIG,
     & PANELXJO, PANELYJO, PANELXJP, PANELYJP, PANELDX, PANELDY,
     & DSO, DSIO, APAN, RX1, RY1, RX2, RY2, SX, SY,
     & X1, X2, YY, RS1, RS2, SGN, G1, G2, T1, T2, X1I, X2I, YYI
1000  FORMAT('{"sequence":',I12,',"runtime":"fortran",'
     & '"kind":"psilin_panel","scope":"',A,
     & '","name":null,"data":{"fieldIndex":',I6,
     & ',"panelIndex":',I6,',"jm":',I6,',"jo":',I6,
     & ',"jp":',I6,',"jq":',I6,
     & ',"computeGeometricSensitivities":',I2,
     & ',"includeSourceTerms":',I2,
     & ',"precision":"Single",'
     & '"panelXJo":',1PE24.16,',"panelYJo":',1PE24.16,
     & ',"panelXJp":',1PE24.16,',"panelYJp":',1PE24.16,
     & ',"panelDx":',1PE24.16,',"panelDy":',1PE24.16,
     & ',"dso":',1PE24.16,',"dsio":',1PE24.16,
     & ',"panelAngle":',1PE24.16,
     & ',"rx1":',1PE24.16,',"ry1":',1PE24.16,
     & ',"rx2":',1PE24.16,',"ry2":',1PE24.16,
     & ',"sx":',1PE24.16,',"sy":',1PE24.16,
     & ',"x1":',1PE24.16,',"x2":',1PE24.16,
     & ',"yy":',1PE24.16,',"rs1":',1PE24.16,
     & ',"rs2":',1PE24.16,',"sgn":',1PE24.16,
     & ',"g1":',1PE24.16,',"g2":',1PE24.16,
     & ',"t1":',1PE24.16,',"t2":',1PE24.16,
     & ',"x1i":',1PE24.16,',"x2i":',1PE24.16,
     & ',"yyi":',1PE24.16,
     & '},"values":null,"tags":null,"timestampUtc":null}')
      RETURN
      END


      SUBROUTINE TRACE_WAKE_STEP_TERMS(SCOPE, INDEX, DS, PREVX, PREVY,
     &                                 NORMALX, NORMALY, XVAL, YVAL)
      CHARACTER*(*) SCOPE
      INTEGER TRACE_LENTRIM, TRACE_NEXTSEQ
      INTEGER LS, SEQ, INDEX
      REAL DS, PREVX, PREVY, NORMALX, NORMALY, XVAL, YVAL
      LOGICAL LOPEN
      CHARACTER*128 CSCOPE
C
      INQUIRE(UNIT=51, OPENED=LOPEN)
      IF(.NOT.LOPEN) RETURN
      CALL TRACE_CLEAN(SCOPE, CSCOPE)
      LS = TRACE_LENTRIM(CSCOPE)
      IF(LS.LE.0) LS = 1
C
      SEQ = TRACE_NEXTSEQ()
      WRITE(51,1000) SEQ, CSCOPE(1:LS), INDEX, DS,
     &               PREVX, PREVY, NORMALX, NORMALY, XVAL, YVAL
 1000 FORMAT('{"sequence":',I12,',"runtime":"fortran",'
     & '"kind":"wake_step_terms","scope":"',A,
     & '","name":null,"data":{"index":',I6,
     & ',"ds":',1PE24.16,',"previousX":',1PE24.16,
     & ',"previousY":',1PE24.16,',"normalX":',1PE24.16,
     & ',"normalY":',1PE24.16,',"x":',1PE24.16,
     & ',"y":',1PE24.16,',"precision":"Single"},'
     & '"values":null,"tags":null,"timestampUtc":null}')
      RETURN
      END


      SUBROUTINE TRACE_PSILIN_ACCUM_STATE(SCOPE, FIELDINDEX, STAGE,
     &     JO, JP, PSIBEFORE, PSINIBEFORE, PSIVAL, PSINIVAL)
      CHARACTER*(*) SCOPE, STAGE
      INTEGER TRACE_LENTRIM, TRACE_NEXTSEQ
      INTEGER LS, LSTAGE, SEQ, FIELDINDEX, JO, JP
      REAL PSIBEFORE, PSINIBEFORE, PSIVAL, PSINIVAL
      LOGICAL LOPEN
      CHARACTER*128 CSCOPE, CSTAGE
C
      INQUIRE(UNIT=51, OPENED=LOPEN)
      IF(.NOT.LOPEN) RETURN
      CALL TRACE_CLEAN(SCOPE, CSCOPE)
      CALL TRACE_CLEAN(STAGE, CSTAGE)
      LS = TRACE_LENTRIM(CSCOPE)
      IF(LS.LE.0) LS = 1
      LSTAGE = TRACE_LENTRIM(CSTAGE)
      IF(LSTAGE.LE.0) LSTAGE = 1
C
      SEQ = TRACE_NEXTSEQ()
      WRITE(51,1000) SEQ, CSCOPE(1:LS), FIELDINDEX, CSTAGE(1:LSTAGE),
     &               JO, JP, PSIBEFORE, PSINIBEFORE, PSIVAL, PSINIVAL
 1000 FORMAT('{"sequence":',I12,',"runtime":"fortran",'
     & '"kind":"psilin_accum_state","scope":"',A,
     & '","name":null,"data":{"fieldIndex":',I6,
     & ',"stage":"',A,'","jo":',I6,',"jp":',I6,
     & ',"psiBefore":',1PE24.16,
     & ',"psiNormalBefore":',1PE24.16,
     & ',"psi":',1PE24.16,',"psiNormalDerivative":',1PE24.16,
     & ',"precision":"Single"},'
     & '"values":null,"tags":null,"timestampUtc":null}')
      RETURN
      END


      SUBROUTINE TRACE_PSILIN_RESULT_TERMS(SCOPE, FIELDINDEX,
     &     PSIBEFORE, PSINIBEFORE, PSIFREESTREAMDELTA,
     &     PSINIFREESTREAMDELTA)
      CHARACTER*(*) SCOPE
      INTEGER TRACE_LENTRIM, TRACE_NEXTSEQ
      INTEGER LS, SEQ, FIELDINDEX
      REAL PSIBEFORE, PSINIBEFORE, PSIFREESTREAMDELTA
      REAL PSINIFREESTREAMDELTA
      LOGICAL LOPEN
      CHARACTER*128 CSCOPE
C
      INQUIRE(UNIT=51, OPENED=LOPEN)
      IF(.NOT.LOPEN) RETURN
      CALL TRACE_CLEAN(SCOPE, CSCOPE)
      LS = TRACE_LENTRIM(CSCOPE)
      IF(LS.LE.0) LS = 1
C
      SEQ = TRACE_NEXTSEQ()
      WRITE(51,1000) SEQ, CSCOPE(1:LS), FIELDINDEX,
     &               PSIBEFORE, PSINIBEFORE,
     &               PSIFREESTREAMDELTA, PSINIFREESTREAMDELTA
 1000 FORMAT('{"sequence":',I12,',"runtime":"fortran",'
     & '"kind":"psilin_result_terms","scope":"',A,
     & '","name":null,"data":{"fieldIndex":',I6,
     & ',"psiBeforeFreestream":',1PE24.16,
     & ',"psiNormalBeforeFreestream":',1PE24.16,
     & ',"psiFreestreamDelta":',1PE24.16,
     & ',"psiNormalFreestreamDelta":',1PE24.16,
     & ',"precision":"Single"},'
     & '"values":null,"tags":null,"timestampUtc":null}')
      RETURN
      END


      SUBROUTINE TRACE_PSILIN_RESULT(SCOPE, FIELDINDEX, PSIVAL,
     &                               PSINIVAL)
      CHARACTER*(*) SCOPE
      INTEGER TRACE_LENTRIM, TRACE_NEXTSEQ
      INTEGER LS, SEQ, FIELDINDEX
      REAL PSIVAL, PSINIVAL
      LOGICAL LOPEN
      CHARACTER*128 CSCOPE
C
      INQUIRE(UNIT=51, OPENED=LOPEN)
      IF(.NOT.LOPEN) RETURN
      CALL TRACE_CLEAN(SCOPE, CSCOPE)
      LS = TRACE_LENTRIM(CSCOPE)
      IF(LS.LE.0) LS = 1
C
      SEQ = TRACE_NEXTSEQ()
      WRITE(51,1000) SEQ, CSCOPE(1:LS), FIELDINDEX,
     &               PSIVAL, PSINIVAL
 1000 FORMAT('{"sequence":',I12,',"runtime":"fortran",'
     & '"kind":"psilin_result","scope":"',A,
     & '","name":null,"data":{"fieldIndex":',I6,
     & ',"psi":',1PE24.16,',"psiNormalDerivative":',1PE24.16,
     & ',"precision":"Single"},'
     & '"values":null,"tags":null,"timestampUtc":null}')
      RETURN
      END


      SUBROUTINE TRACE_BUFFER_NODE(SCOPE, INDEX, XVAL, YVAL, SVAL)
      CHARACTER*(*) SCOPE
      INTEGER TRACE_LENTRIM, TRACE_NEXTSEQ
      INTEGER LS, SEQ, INDEX
      REAL XVAL, YVAL, SVAL
      LOGICAL LOPEN
      CHARACTER*128 CSCOPE
C
      INQUIRE(UNIT=51, OPENED=LOPEN)
      IF(.NOT.LOPEN) RETURN
      CALL TRACE_CLEAN(SCOPE, CSCOPE)
      LS = TRACE_LENTRIM(CSCOPE)
      IF(LS.LE.0) LS = 1
C
      SEQ = TRACE_NEXTSEQ()
      WRITE(51,1000) SEQ, CSCOPE(1:LS), INDEX, XVAL, YVAL, SVAL
 1000 FORMAT('{"sequence":',I12,',"runtime":"fortran",'
     & '"kind":"buffer_node","scope":"',A,
     & '","name":null,"data":{"index":',I6,
     & ',"x":',1PE24.16,',"y":',1PE24.16,
     & ',"arcLength":',1PE24.16,
     & '},"values":null,"tags":null,"timestampUtc":null}')
      RETURN
      END


      SUBROUTINE TRACE_NACA4_NODE(SCOPE, INDEX, FRAC, ONEMF,
     & USEDTE, SQRTONEMF, XPOWAN, XPOWANP, XTERM1, XTERM2,
     & XVAL, X2VAL, X3VAL, X4VAL, SQRTX, YTSCALE,
     & YTTERM0, YTTERM1, YTTERM2, YTTERM3, YTTERM4,
     & YTPART1, YTPART2, YTPART3, YTPOLY, YTVAL,
     & CAMBERBRANCH, YCVAL, DYCDX)
      CHARACTER*(*) SCOPE
      INTEGER TRACE_LENTRIM, TRACE_NEXTSEQ
      INTEGER LS, SEQ, INDEX, USEDTE, CAMBERBRANCH
      REAL FRAC, ONEMF, SQRTONEMF, XPOWAN, XPOWANP
      REAL XTERM1, XTERM2, XVAL, X2VAL, X3VAL, X4VAL, SQRTX, YTSCALE
      REAL YTTERM0, YTTERM1, YTTERM2, YTTERM3, YTTERM4
      REAL YTPART1, YTPART2, YTPART3, YTPOLY, YTVAL
      REAL YCVAL, DYCDX
      LOGICAL LOPEN
      CHARACTER*128 CSCOPE
C
      INQUIRE(UNIT=51, OPENED=LOPEN)
      IF(.NOT.LOPEN) RETURN
      CALL TRACE_CLEAN(SCOPE, CSCOPE)
      LS = TRACE_LENTRIM(CSCOPE)
      IF(LS.LE.0) LS = 1
C
      SEQ = TRACE_NEXTSEQ()
      WRITE(51,1000) SEQ, CSCOPE(1:LS), INDEX, FRAC, ONEMF, USEDTE,
     &               SQRTONEMF, XPOWAN, XPOWANP, XTERM1, XTERM2,
     &               XVAL, X2VAL, X3VAL, X4VAL, SQRTX, YTSCALE,
     &               YTTERM0, YTTERM1, YTTERM2, YTTERM3, YTTERM4,
     &               YTPART1, YTPART2, YTPART3, YTPOLY, YTVAL,
     &               CAMBERBRANCH, YCVAL, DYCDX
 1000 FORMAT('{"sequence":',I12,',"runtime":"fortran",'
     & '"kind":"naca4_node","scope":"',A,
     & '","name":null,"data":{"index":',I6,
     & ',"fraction":',1PE24.16,',"oneMinusFraction":',1PE24.16,
     & ',"usedTrailingEdgeOverride":',I2,
     & ',"sqrtOneMinusFraction":',1PE24.16,
     & ',"xPowerAn":',1PE24.16,',"xPowerAnp":',1PE24.16,
     & ',"xLeadingTerm":',1PE24.16,',"xTrailingTerm":',1PE24.16,
     & ',"x":',1PE24.16,',"x2":',1PE24.16,
     & ',"x3":',1PE24.16,',"x4":',1PE24.16,
     & ',"sqrtX":',1PE24.16,',"ytScale":',1PE24.16,
     & ',"ytTermSqrt":',1PE24.16,',"ytTermX":',1PE24.16,
     & ',"ytTermX2":',1PE24.16,',"ytTermX3":',1PE24.16,
     & ',"ytTermX4":',1PE24.16,',"ytPartial1":',1PE24.16,
     & ',"ytPartial2":',1PE24.16,',"ytPartial3":',1PE24.16,
     & ',"ytPolynomial":',1PE24.16,',"yt":',1PE24.16,
     & ',"camberBranch":',I2,',"yc":',1PE24.16,
     & ',"dycDx":',1PE24.16,
     & ',"useClassicXFoilGeometry":1,"precision":"Single"},'
     & '"values":null,"tags":null,"timestampUtc":null}')
      RETURN
      END


      SUBROUTINE TRACE_NACA4_CONFIG(SCOPE, IDES, NSIDE, MVAL, PVAL,
     & TVAL)
      CHARACTER*(*) SCOPE
      INTEGER TRACE_LENTRIM, TRACE_NEXTSEQ
      INTEGER LS, SEQ, IDES, NSIDE
      REAL MVAL, PVAL, TVAL
      LOGICAL LOPEN
      CHARACTER*128 CSCOPE
C
      INQUIRE(UNIT=51, OPENED=LOPEN)
      IF(.NOT.LOPEN) RETURN
      CALL TRACE_CLEAN(SCOPE, CSCOPE)
      LS = TRACE_LENTRIM(CSCOPE)
      IF(LS.LE.0) LS = 1
C
      SEQ = TRACE_NEXTSEQ()
      WRITE(51,1000) SEQ, CSCOPE(1:LS), IDES, NSIDE, MVAL, PVAL,
     &               TVAL
 1000 FORMAT('{"sequence":',I12,',"runtime":"fortran",'
     & '"kind":"naca4_config","scope":"',A,
     & '","name":null,"data":{"ides":',I6,
     & ',"pointCount":',I6,',"maxCamber":',1PE24.16,
     & ',"maxCamberPosition":',1PE24.16,
     & ',"thickness":',1PE24.16,
     & ',"useClassicXFoilGeometry":1,"precision":"Single"},'
     & '"values":null,"tags":null,"timestampUtc":null}')
      RETURN
      END


      SUBROUTINE TRACE_NACA_GEOMETRY_POINT(SCOPE, INDEX, XVAL, YVAL)
      CHARACTER*(*) SCOPE
      INTEGER TRACE_LENTRIM, TRACE_NEXTSEQ
      INTEGER LS, SEQ, INDEX
      REAL XVAL, YVAL
      LOGICAL LOPEN
      CHARACTER*128 CSCOPE
C
      INQUIRE(UNIT=51, OPENED=LOPEN)
      IF(.NOT.LOPEN) RETURN
      CALL TRACE_CLEAN(SCOPE, CSCOPE)
      LS = TRACE_LENTRIM(CSCOPE)
      IF(LS.LE.0) LS = 1
C
      SEQ = TRACE_NEXTSEQ()
      WRITE(51,1000) SEQ, CSCOPE(1:LS), INDEX, XVAL, YVAL
 1000 FORMAT('{"sequence":',I12,',"runtime":"fortran",'
     & '"kind":"naca_geometry_point","scope":"',A,
     & '","name":null,"data":{"index":',I6,
     & ',"x":',1PE24.16,',"y":',1PE24.16,
     & '},"values":null,"tags":null,"timestampUtc":null}')
      RETURN
      END


      SUBROUTINE TRACE_BUFFER_SPLINE_NODE(SCOPE, INDEX, XVAL, YVAL,
     &                                    SVAL, XPVAL, YPVAL)
      CHARACTER*(*) SCOPE
      INTEGER TRACE_LENTRIM, TRACE_NEXTSEQ
      INTEGER LS, SEQ, INDEX
      REAL XVAL, YVAL, SVAL, XPVAL, YPVAL
      LOGICAL LOPEN
      CHARACTER*128 CSCOPE
C
      INQUIRE(UNIT=51, OPENED=LOPEN)
      IF(.NOT.LOPEN) RETURN
      CALL TRACE_CLEAN(SCOPE, CSCOPE)
      LS = TRACE_LENTRIM(CSCOPE)
      IF(LS.LE.0) LS = 1
C
      SEQ = TRACE_NEXTSEQ()
      WRITE(51,1000) SEQ, CSCOPE(1:LS), INDEX, XVAL, YVAL, SVAL,
     &               XPVAL, YPVAL
 1000 FORMAT('{"sequence":',I12,',"runtime":"fortran",'
     & '"kind":"buffer_spline_node","scope":"',A,
     & '","name":null,"data":{"index":',I6,
     & ',"x":',1PE24.16,',"y":',1PE24.16,
     & ',"arcLength":',1PE24.16,',"xp":',1PE24.16,
     & ',"yp":',1PE24.16,
     & '},"values":null,"tags":null,"timestampUtc":null}')
      RETURN
      END


      SUBROUTINE TRACE_SPLINE_SEGMENT(SCOPE, ROUTINE, SEGSTART,
     &                                SEGCOUNT, STARTBC, ENDBC,
     &                                PRECISION)
      CHARACTER*(*) SCOPE, ROUTINE, STARTBC, ENDBC, PRECISION
      INTEGER TRACE_LENTRIM, TRACE_NEXTSEQ
      INTEGER LS, LR, LB1, LB2, LP, SEQ, SEGSTART, SEGCOUNT
      LOGICAL LOPEN
      CHARACTER*128 CSCOPE, CROUTINE
      CHARACTER*64 CSTARTBC, CENDBC, CPRECISION
C
      INQUIRE(UNIT=51, OPENED=LOPEN)
      IF(.NOT.LOPEN) RETURN
      CALL TRACE_CLEAN(SCOPE, CSCOPE)
      CALL TRACE_CLEAN(ROUTINE, CROUTINE)
      CALL TRACE_CLEAN(STARTBC, CSTARTBC)
      CALL TRACE_CLEAN(ENDBC, CENDBC)
      CALL TRACE_CLEAN(PRECISION, CPRECISION)
      LS = TRACE_LENTRIM(CSCOPE)
      LR = TRACE_LENTRIM(CROUTINE)
      LB1 = TRACE_LENTRIM(CSTARTBC)
      LB2 = TRACE_LENTRIM(CENDBC)
      LP = TRACE_LENTRIM(CPRECISION)
      IF(LS.LE.0) LS = 1
      IF(LR.LE.0) LR = 1
      IF(LB1.LE.0) LB1 = 1
      IF(LB2.LE.0) LB2 = 1
      IF(LP.LE.0) LP = 1
C
      SEQ = TRACE_NEXTSEQ()
      WRITE(51,1000) SEQ, CSCOPE(1:LS), CROUTINE(1:LR),
     &               SEGSTART, SEGCOUNT, CSTARTBC(1:LB1),
     &               CENDBC(1:LB2), CPRECISION(1:LP)
 1000 FORMAT('{"sequence":',I12,',"runtime":"fortran",'
     & '"kind":"spline_segment","scope":"',A,
     & '","name":null,"data":{"routine":"',A,
     & '","segmentStart":',I6,',"segmentCount":',I6,
     & ',"startBoundaryCondition":"',A,
     & '","endBoundaryCondition":"',A,
     & '","precision":"',A,
     & '"},"values":null,"tags":null,"timestampUtc":null}')
      RETURN
      END


      SUBROUTINE TRACE_SPLINE_SYSTEM_ROW(SCOPE, ROUTINE, INDEX,
     & VALUE, PARAMETER, LOWER, DIAGONAL, UPPER, RHS,
     & STARTBC, ENDBC, PRECISION)
      CHARACTER*(*) SCOPE, ROUTINE, STARTBC, ENDBC, PRECISION
      INTEGER TRACE_LENTRIM, TRACE_NEXTSEQ
      INTEGER LS, LR, LB1, LB2, LP, SEQ, INDEX
      REAL VALUE, PARAMETER, LOWER, DIAGONAL, UPPER, RHS
      LOGICAL LOPEN
      CHARACTER*128 CSCOPE, CROUTINE
      CHARACTER*64 CSTARTBC, CENDBC, CPRECISION
C
      INQUIRE(UNIT=51, OPENED=LOPEN)
      IF(.NOT.LOPEN) RETURN
      CALL TRACE_CLEAN(SCOPE, CSCOPE)
      CALL TRACE_CLEAN(ROUTINE, CROUTINE)
      CALL TRACE_CLEAN(STARTBC, CSTARTBC)
      CALL TRACE_CLEAN(ENDBC, CENDBC)
      CALL TRACE_CLEAN(PRECISION, CPRECISION)
      LS = TRACE_LENTRIM(CSCOPE)
      LR = TRACE_LENTRIM(CROUTINE)
      LB1 = TRACE_LENTRIM(CSTARTBC)
      LB2 = TRACE_LENTRIM(CENDBC)
      LP = TRACE_LENTRIM(CPRECISION)
      IF(LS.LE.0) LS = 1
      IF(LR.LE.0) LR = 1
      IF(LB1.LE.0) LB1 = 1
      IF(LB2.LE.0) LB2 = 1
      IF(LP.LE.0) LP = 1
C
      SEQ = TRACE_NEXTSEQ()
      WRITE(51,1000) SEQ, CSCOPE(1:LS), CROUTINE(1:LR), INDEX,
     &               VALUE, PARAMETER, LOWER, DIAGONAL, UPPER, RHS,
     &               CSTARTBC(1:LB1), CENDBC(1:LB2), CPRECISION(1:LP)
 1000 FORMAT('{"sequence":',I12,',"runtime":"fortran",'
     & '"kind":"spline_system_row","scope":"',A,
     & '","name":null,"data":{"routine":"',A,
     & '","index":',I6,',"value":',1PE24.16,
     & ',"parameter":',1PE24.16,',"lower":',1PE24.16,
     & ',"diagonal":',1PE24.16,',"upper":',1PE24.16,
     & ',"rhs":',1PE24.16,
     & ',"startBoundaryCondition":"',A,
     & '","endBoundaryCondition":"',A,
     & '","precision":"',A,
     & '"},"values":null,"tags":null,"timestampUtc":null}')
      RETURN
      END


      SUBROUTINE TRACE_SPLINE_SOLUTION_NODE(SCOPE, ROUTINE, INDEX,
     & VALUE, PARAMETER, DERIVATIVE, STARTBC, ENDBC, PRECISION)
      CHARACTER*(*) SCOPE, ROUTINE, STARTBC, ENDBC, PRECISION
      INTEGER TRACE_LENTRIM, TRACE_NEXTSEQ
      INTEGER LS, LR, LB1, LB2, LP, SEQ, INDEX
      REAL VALUE, PARAMETER, DERIVATIVE
      LOGICAL LOPEN
      CHARACTER*128 CSCOPE, CROUTINE
      CHARACTER*64 CSTARTBC, CENDBC, CPRECISION
C
      INQUIRE(UNIT=51, OPENED=LOPEN)
      IF(.NOT.LOPEN) RETURN
      CALL TRACE_CLEAN(SCOPE, CSCOPE)
      CALL TRACE_CLEAN(ROUTINE, CROUTINE)
      CALL TRACE_CLEAN(STARTBC, CSTARTBC)
      CALL TRACE_CLEAN(ENDBC, CENDBC)
      CALL TRACE_CLEAN(PRECISION, CPRECISION)
      LS = TRACE_LENTRIM(CSCOPE)
      LR = TRACE_LENTRIM(CROUTINE)
      LB1 = TRACE_LENTRIM(CSTARTBC)
      LB2 = TRACE_LENTRIM(CENDBC)
      LP = TRACE_LENTRIM(CPRECISION)
      IF(LS.LE.0) LS = 1
      IF(LR.LE.0) LR = 1
      IF(LB1.LE.0) LB1 = 1
      IF(LB2.LE.0) LB2 = 1
      IF(LP.LE.0) LP = 1
C
      SEQ = TRACE_NEXTSEQ()
      WRITE(51,1000) SEQ, CSCOPE(1:LS), CROUTINE(1:LR), INDEX,
     &               VALUE, PARAMETER, DERIVATIVE, CSTARTBC(1:LB1),
     &               CENDBC(1:LB2), CPRECISION(1:LP)
 1000 FORMAT('{"sequence":',I12,',"runtime":"fortran",'
     & '"kind":"spline_solution_node","scope":"',A,
     & '","name":null,"data":{"routine":"',A,
     & '","index":',I6,',"value":',1PE24.16,
     & ',"parameter":',1PE24.16,',"derivative":',1PE24.16,
     & ',"startBoundaryCondition":"',A,
     & '","endBoundaryCondition":"',A,
     & '","precision":"',A,
     & '"},"values":null,"tags":null,"timestampUtc":null}')
      RETURN
      END


      SUBROUTINE TRACE_ARC_LENGTH_STEP(SCOPE, ROUTINE, INDEX,
     & DXVAL, DYVAL, SEGMENTLENGTH, CUMULATIVE, PRECISION)
      CHARACTER*(*) SCOPE, ROUTINE, PRECISION
      INTEGER TRACE_LENTRIM, TRACE_NEXTSEQ
      INTEGER LS, LR, LP, SEQ, INDEX
      REAL DXVAL, DYVAL, SEGMENTLENGTH, CUMULATIVE
      LOGICAL LOPEN
      CHARACTER*128 CSCOPE, CROUTINE
      CHARACTER*64 CPRECISION
C
      INQUIRE(UNIT=51, OPENED=LOPEN)
      IF(.NOT.LOPEN) RETURN
      CALL TRACE_CLEAN(SCOPE, CSCOPE)
      CALL TRACE_CLEAN(ROUTINE, CROUTINE)
      CALL TRACE_CLEAN(PRECISION, CPRECISION)
      LS = TRACE_LENTRIM(CSCOPE)
      LR = TRACE_LENTRIM(CROUTINE)
      LP = TRACE_LENTRIM(CPRECISION)
      IF(LS.LE.0) LS = 1
      IF(LR.LE.0) LR = 1
      IF(LP.LE.0) LP = 1
C
      SEQ = TRACE_NEXTSEQ()
      WRITE(51,1000) SEQ, CSCOPE(1:LS), CROUTINE(1:LR), INDEX,
     &               DXVAL, DYVAL, SEGMENTLENGTH, CUMULATIVE,
     &               CPRECISION(1:LP)
 1000 FORMAT('{"sequence":',I12,',"runtime":"fortran",'
     & '"kind":"arc_length_step","scope":"',A,
     & '","name":null,"data":{"routine":"',A,
     & '","index":',I6,',"dx":',1PE24.16,
     & ',"dy":',1PE24.16,',"segmentLength":',1PE24.16,
     & ',"cumulative":',1PE24.16,',"precision":"',A,
     & '"},"values":null,"tags":null,"timestampUtc":null}')
      RETURN
      END


      SUBROUTINE TRACE_CURVATURE_EVAL(SCOPE, ROUTINE, LOWERINDEX,
     & UPPERINDEX, PARAMETER, DSVAL, TVAL, XDELTA, YDELTA,
     & CX1, CX2, CY1, CY2,
     & XFACTOR1, XFACTOR2, YFACTOR1, YFACTOR2,
     & XTERM1, XTERM2, YTERM1, YTERM2,
     & XD, XDD, YD, YDD, SDVAL, CURVATURE, PRECISION)
      CHARACTER*(*) SCOPE, ROUTINE, PRECISION
      INTEGER TRACE_LENTRIM, TRACE_NEXTSEQ
      INTEGER LS, LR, LP, SEQ, LOWERINDEX, UPPERINDEX
      REAL PARAMETER, DSVAL, TVAL, XDELTA, YDELTA, CX1, CX2
      REAL CY1, CY2, XFACTOR1, XFACTOR2, YFACTOR1, YFACTOR2
      REAL XTERM1, XTERM2, YTERM1, YTERM2
      REAL XD, XDD, YD, YDD, SDVAL, CURVATURE
      LOGICAL LOPEN
      CHARACTER*128 CSCOPE, CROUTINE
      CHARACTER*64 CPRECISION
C
      INQUIRE(UNIT=51, OPENED=LOPEN)
      IF(.NOT.LOPEN) RETURN
      CALL TRACE_CLEAN(SCOPE, CSCOPE)
      CALL TRACE_CLEAN(ROUTINE, CROUTINE)
      CALL TRACE_CLEAN(PRECISION, CPRECISION)
      LS = TRACE_LENTRIM(CSCOPE)
      LR = TRACE_LENTRIM(CROUTINE)
      LP = TRACE_LENTRIM(CPRECISION)
      IF(LS.LE.0) LS = 1
      IF(LR.LE.0) LR = 1
      IF(LP.LE.0) LP = 1
C
      SEQ = TRACE_NEXTSEQ()
      WRITE(51,1000) SEQ, CSCOPE(1:LS), CROUTINE(1:LR),
     &               LOWERINDEX, UPPERINDEX, PARAMETER, DSVAL, TVAL,
     &               XDELTA, YDELTA, CX1, CX2, CY1, CY2,
     &               XFACTOR1, XFACTOR2, YFACTOR1, YFACTOR2,
     &               XTERM1, XTERM2, YTERM1, YTERM2,
     &               XD, XDD, YD, YDD, SDVAL, CURVATURE,
     &               CPRECISION(1:LP)
 1000 FORMAT('{"sequence":',I12,',"runtime":"fortran",'
     & '"kind":"curvature_eval","scope":"',A,
     & '","name":null,"data":{"routine":"',A,
     & '","lowerIndex":',I6,',"upperIndex":',I6,
     & ',"parameter":',1PE24.16,',"ds":',1PE24.16,
     & ',"t":',1PE24.16,',"xDelta":',1PE24.16,
     & ',"yDelta":',1PE24.16,',"cx1":',1PE24.16,
     & ',"cx2":',1PE24.16,',"cy1":',1PE24.16,
     & ',"cy2":',1PE24.16,',"xFactor1":',1PE24.16,
     & ',"xFactor2":',1PE24.16,',"yFactor1":',1PE24.16,
     & ',"yFactor2":',1PE24.16,',"xTerm1":',1PE24.16,
     & ',"xTerm2":',1PE24.16,',"yTerm1":',1PE24.16,
     & ',"yTerm2":',1PE24.16,',"xd":',1PE24.16,
     & ',"xdd":',1PE24.16,',"yd":',1PE24.16,
     & ',"ydd":',1PE24.16,',"sd":',1PE24.16,
     & ',"curvature":',1PE24.16,',"precision":"',A,
     & '"},"values":null,"tags":null,"timestampUtc":null}')
      RETURN
      END


      SUBROUTINE TRACE_SPLINE_EVAL(SCOPE, ROUTINE, LOWERINDEX,
     & UPPERINDEX, PARAMETER, DSVAL, TVAL, VALUELOW, VALUEHIGH,
     & DERIVATIVELOW, DERIVATIVEHIGH, CX1, CX2, DELTA, FACTOR1,
     & FACTOR2, PRODUCT1, PRODUCT2, OPERAND1, OPERAND2,
     & OPERANDCOMBINED, ACCUMULATOR, VALUE, PRECISION)
      CHARACTER*(*) SCOPE, ROUTINE, PRECISION
      INTEGER TRACE_LENTRIM, TRACE_NEXTSEQ
      INTEGER LS, LR, LP, SEQ, LOWERINDEX, UPPERINDEX
      REAL PARAMETER, DSVAL, TVAL, VALUELOW, VALUEHIGH
      REAL DERIVATIVELOW, DERIVATIVEHIGH, CX1, CX2, DELTA
      REAL FACTOR1, FACTOR2, PRODUCT1, PRODUCT2, OPERAND1
      REAL OPERAND2, OPERANDCOMBINED, ACCUMULATOR
      REAL VALUE
      LOGICAL LOPEN
      CHARACTER*128 CSCOPE, CROUTINE
      CHARACTER*64 CPRECISION
C
      INQUIRE(UNIT=51, OPENED=LOPEN)
      IF(.NOT.LOPEN) RETURN
      CALL TRACE_CLEAN(SCOPE, CSCOPE)
      CALL TRACE_CLEAN(ROUTINE, CROUTINE)
      CALL TRACE_CLEAN(PRECISION, CPRECISION)
      LS = TRACE_LENTRIM(CSCOPE)
      LR = TRACE_LENTRIM(CROUTINE)
      LP = TRACE_LENTRIM(CPRECISION)
      IF(LS.LE.0) LS = 1
      IF(LR.LE.0) LR = 1
      IF(LP.LE.0) LP = 1
C
      SEQ = TRACE_NEXTSEQ()
      WRITE(51,1000) SEQ, CSCOPE(1:LS), CROUTINE(1:LR),
     &               LOWERINDEX, UPPERINDEX, PARAMETER, DSVAL,
     &               TVAL, VALUELOW, VALUEHIGH, DERIVATIVELOW,
     &               DERIVATIVEHIGH, CX1, CX2, DELTA, FACTOR1,
     &               FACTOR2, PRODUCT1, PRODUCT2, OPERAND1,
     &               OPERAND2, OPERANDCOMBINED, ACCUMULATOR,
     &               VALUE, CPRECISION(1:LP)
 1000 FORMAT('{"sequence":',I12,',"runtime":"fortran",'
     & '"kind":"spline_eval","scope":"',A,
     & '","name":null,"data":{"routine":"',A,
     & '","lowerIndex":',I6,',"upperIndex":',I6,
     & ',"parameter":',1PE24.16,',"ds":',1PE24.16,
     & ',"t":',1PE24.16,',"valueLow":',1PE24.16,
     & ',"valueHigh":',1PE24.16,',"derivativeLow":',1PE24.16,
     & ',"derivativeHigh":',1PE24.16,',"cx1":',1PE24.16,
     & ',"cx2":',1PE24.16,',"delta":',1PE24.16,
     & ',"factor1":',1PE24.16,',"factor2":',1PE24.16,
     & ',"product1":',1PE24.16,',"product2":',1PE24.16,
     & ',"operand1":',1PE24.16,',"operand2":',1PE24.16,
     & ',"operandCombined":',1PE24.16,',"accumulator":',1PE24.16,
     & ',"value":',1PE24.16,
     & ',"precision":"',A,
     & '"},"values":null,"tags":null,"timestampUtc":null}')
      RETURN
      END


      SUBROUTINE TRACE_TRIDIAGONAL_FORWARD(SCOPE, ROUTINE, INDEX,
     & PIVOT, LOWER, UPPERBEFORE, RHSBEFOREPIVOT, UPPERAFTER,
     & RHSAFTERPIVOT, DIAGONALBEFORE, DIAGONALAFTER,
     & RHSBEFORE, RHSAFTER, PRECISION)
      CHARACTER*(*) SCOPE, ROUTINE, PRECISION
      INTEGER TRACE_LENTRIM, TRACE_NEXTSEQ
      INTEGER LS, LR, LP, SEQ, INDEX
      REAL PIVOT, LOWER, UPPERBEFORE, RHSBEFOREPIVOT, UPPERAFTER
      REAL RHSAFTERPIVOT, DIAGONALBEFORE, DIAGONALAFTER
      REAL RHSBEFORE, RHSAFTER
      LOGICAL LOPEN
      CHARACTER*128 CSCOPE, CROUTINE
      CHARACTER*64 CPRECISION
C
      INQUIRE(UNIT=51, OPENED=LOPEN)
      IF(.NOT.LOPEN) RETURN
      CALL TRACE_CLEAN(SCOPE, CSCOPE)
      CALL TRACE_CLEAN(ROUTINE, CROUTINE)
      CALL TRACE_CLEAN(PRECISION, CPRECISION)
      LS = TRACE_LENTRIM(CSCOPE)
      LR = TRACE_LENTRIM(CROUTINE)
      LP = TRACE_LENTRIM(CPRECISION)
      IF(LS.LE.0) LS = 1
      IF(LR.LE.0) LR = 1
      IF(LP.LE.0) LP = 1
C
      SEQ = TRACE_NEXTSEQ()
      WRITE(51,1000) SEQ, CSCOPE(1:LS), CROUTINE(1:LR), INDEX,
     &               PIVOT, LOWER, UPPERBEFORE, RHSBEFOREPIVOT,
     &               UPPERAFTER, RHSAFTERPIVOT, DIAGONALBEFORE,
     &               DIAGONALAFTER, RHSBEFORE, RHSAFTER,
     &               CPRECISION(1:LP)
 1000 FORMAT('{"sequence":',I12,',"runtime":"fortran",'
     & '"kind":"tridiagonal_forward","scope":"',A,
     & '","name":null,"data":{"routine":"',A,
     & '","index":',I6,',"pivot":',1PE24.16,
     & ',"lower":',1PE24.16,',"upperBefore":',1PE24.16,
     & ',"rhsBeforePivot":',1PE24.16,',"upperAfter":',1PE24.16,
     & ',"rhsAfterPivot":',1PE24.16,
     & ',"diagonalBefore":',1PE24.16,
     & ',"diagonalAfter":',1PE24.16,
     & ',"rhsBefore":',1PE24.16,',"rhsAfter":',1PE24.16,
     & ',"precision":"',A,
     & '"},"values":null,"tags":null,"timestampUtc":null}')
      RETURN
      END


      SUBROUTINE TRACE_TRIDIAGONAL_LAST_PIVOT(SCOPE, ROUTINE, INDEX,
     &                                        PIVOT, RHSBEFORE,
     &                                        RHSAFTER, PRECISION)
      CHARACTER*(*) SCOPE, ROUTINE, PRECISION
      INTEGER TRACE_LENTRIM, TRACE_NEXTSEQ
      INTEGER LS, LR, LP, SEQ, INDEX
      REAL PIVOT, RHSBEFORE, RHSAFTER
      LOGICAL LOPEN
      CHARACTER*128 CSCOPE, CROUTINE
      CHARACTER*64 CPRECISION
C
      INQUIRE(UNIT=51, OPENED=LOPEN)
      IF(.NOT.LOPEN) RETURN
      CALL TRACE_CLEAN(SCOPE, CSCOPE)
      CALL TRACE_CLEAN(ROUTINE, CROUTINE)
      CALL TRACE_CLEAN(PRECISION, CPRECISION)
      LS = TRACE_LENTRIM(CSCOPE)
      LR = TRACE_LENTRIM(CROUTINE)
      LP = TRACE_LENTRIM(CPRECISION)
      IF(LS.LE.0) LS = 1
      IF(LR.LE.0) LR = 1
      IF(LP.LE.0) LP = 1
C
      SEQ = TRACE_NEXTSEQ()
      WRITE(51,1000) SEQ, CSCOPE(1:LS), CROUTINE(1:LR), INDEX,
     &               PIVOT, RHSBEFORE, RHSAFTER, CPRECISION(1:LP)
 1000 FORMAT('{"sequence":',I12,',"runtime":"fortran",'
     & '"kind":"tridiagonal_last_pivot","scope":"',A,
     & '","name":null,"data":{"routine":"',A,
     & '","index":',I6,',"pivot":',1PE24.16,
     & ',"rhsBefore":',1PE24.16,',"rhsAfter":',1PE24.16,
     & ',"precision":"',A,
     & '"},"values":null,"tags":null,"timestampUtc":null}')
      RETURN
      END


      SUBROUTINE TRACE_TRIDIAGONAL_BACK(SCOPE, ROUTINE, INDEX, UPPER,
     &                                  NEXTVALUE, RHSBEFORE,
     &                                  RHSAFTER, PRECISION)
      CHARACTER*(*) SCOPE, ROUTINE, PRECISION
      INTEGER TRACE_LENTRIM, TRACE_NEXTSEQ
      INTEGER LS, LR, LP, SEQ, INDEX
      REAL UPPER, NEXTVALUE, RHSBEFORE, RHSAFTER
      LOGICAL LOPEN
      CHARACTER*128 CSCOPE, CROUTINE
      CHARACTER*64 CPRECISION
C
      INQUIRE(UNIT=51, OPENED=LOPEN)
      IF(.NOT.LOPEN) RETURN
      CALL TRACE_CLEAN(SCOPE, CSCOPE)
      CALL TRACE_CLEAN(ROUTINE, CROUTINE)
      CALL TRACE_CLEAN(PRECISION, CPRECISION)
      LS = TRACE_LENTRIM(CSCOPE)
      LR = TRACE_LENTRIM(CROUTINE)
      LP = TRACE_LENTRIM(CPRECISION)
      IF(LS.LE.0) LS = 1
      IF(LR.LE.0) LR = 1
      IF(LP.LE.0) LP = 1
C
      SEQ = TRACE_NEXTSEQ()
      WRITE(51,1000) SEQ, CSCOPE(1:LS), CROUTINE(1:LR), INDEX,
     &               UPPER, NEXTVALUE, RHSBEFORE, RHSAFTER,
     &               CPRECISION(1:LP)
 1000 FORMAT('{"sequence":',I12,',"runtime":"fortran",'
     & '"kind":"tridiagonal_back","scope":"',A,
     & '","name":null,"data":{"routine":"',A,
     & '","index":',I6,',"upper":',1PE24.16,
     & ',"nextValue":',1PE24.16,',"rhsBefore":',1PE24.16,
     & ',"rhsAfter":',1PE24.16,',"precision":"',A,
     & '"},"values":null,"tags":null,"timestampUtc":null}')
      RETURN
      END


      SUBROUTINE TRACE_PANGEN_CURVATURE_NODE(SCOPE, STAGE, INDEX,
     &                                       SVAL, VALUE)
      CHARACTER*(*) SCOPE, STAGE
      INTEGER TRACE_LENTRIM, TRACE_NEXTSEQ
      INTEGER LS, LT, SEQ, INDEX
      REAL SVAL, VALUE
      LOGICAL LOPEN
      CHARACTER*128 CSCOPE
      CHARACTER*64 CSTAGE
C
      INQUIRE(UNIT=51, OPENED=LOPEN)
      IF(.NOT.LOPEN) RETURN
      CALL TRACE_CLEAN(SCOPE, CSCOPE)
      CALL TRACE_CLEAN(STAGE, CSTAGE)
      LS = TRACE_LENTRIM(CSCOPE)
      LT = TRACE_LENTRIM(CSTAGE)
      IF(LS.LE.0) LS = 1
      IF(LT.LE.0) LT = 1
C
      SEQ = TRACE_NEXTSEQ()
      WRITE(51,1000) SEQ, CSCOPE(1:LS), CSTAGE(1:LT), INDEX,
     &               SVAL, VALUE
 1000 FORMAT('{"sequence":',I12,',"runtime":"fortran",'
     & '"kind":"pangen_curvature_node","scope":"',A,
     & '","name":null,"data":{"stage":"',A,
     & '","index":',I6,',"arcLength":',1PE24.16,
     & ',"value":',1PE24.16,
     & '},"values":null,"tags":null,"timestampUtc":null}')
      RETURN
      END


      SUBROUTINE TRACE_PANGEN_PANEL_NODE(SCOPE, INDEX, XVAL, YVAL,
     &                                   SVAL, XPVAL, YPVAL)
      CHARACTER*(*) SCOPE
      INTEGER TRACE_LENTRIM, TRACE_NEXTSEQ
      INTEGER LS, SEQ, INDEX
      REAL XVAL, YVAL, SVAL, XPVAL, YPVAL
      LOGICAL LOPEN
      CHARACTER*128 CSCOPE
C
      INQUIRE(UNIT=51, OPENED=LOPEN)
      IF(.NOT.LOPEN) RETURN
      CALL TRACE_CLEAN(SCOPE, CSCOPE)
      LS = TRACE_LENTRIM(CSCOPE)
      IF(LS.LE.0) LS = 1
C
      SEQ = TRACE_NEXTSEQ()
      WRITE(51,1000) SEQ, CSCOPE(1:LS), INDEX, XVAL, YVAL, SVAL,
     &               XPVAL, YPVAL
 1000 FORMAT('{"sequence":',I12,',"runtime":"fortran",'
     & '"kind":"pangen_panel_node","scope":"',A,
     & '","name":null,"data":{"index":',I6,
     & ',"x":',1PE24.16,',"y":',1PE24.16,
     & ',"arcLength":',1PE24.16,',"xp":',1PE24.16,
     & ',"yp":',1PE24.16,
     & '},"values":null,"tags":null,"timestampUtc":null}')
      RETURN
      END


      SUBROUTINE TRACE_PANGEN_LEFIND(SCOPE, STAGE, SLE, XLE, YLE,
     & XTE, YTE, CVLE, CVAVG, IBLE)
      CHARACTER*(*) SCOPE, STAGE
      INTEGER TRACE_LENTRIM, TRACE_NEXTSEQ
      INTEGER LS, LT, SEQ, IBLE
      REAL SLE, XLE, YLE, XTE, YTE, CVLE, CVAVG
      LOGICAL LOPEN
      CHARACTER*128 CSCOPE
      CHARACTER*64 CSTAGE
C
      INQUIRE(UNIT=51, OPENED=LOPEN)
      IF(.NOT.LOPEN) RETURN
      CALL TRACE_CLEAN(SCOPE, CSCOPE)
      CALL TRACE_CLEAN(STAGE, CSTAGE)
      LS = TRACE_LENTRIM(CSCOPE)
      LT = TRACE_LENTRIM(CSTAGE)
      IF(LS.LE.0) LS = 1
      IF(LT.LE.0) LT = 1
C
      SEQ = TRACE_NEXTSEQ()
      WRITE(51,1000) SEQ, CSCOPE(1:LS), CSTAGE(1:LT), SLE,
     &               XLE, YLE, XTE, YTE, CVLE, CVAVG, IBLE
 1000 FORMAT('{"sequence":',I12,',"runtime":"fortran",'
     & '"kind":"pangen_lefind","scope":"',A,
     & '","name":null,"data":{"stage":"',A,
     & '","sle":',1PE24.16,',"xle":',1PE24.16,
     & ',"yle":',1PE24.16,',"xte":',1PE24.16,
     & ',"yte":',1PE24.16,',"cvle":',1PE24.16,
     & ',"cvavg":',1PE24.16,',"ible":',I6,
     & '},"values":null,"tags":null,"timestampUtc":null}')
      RETURN
      END


      SUBROUTINE TRACE_PANGEN_LE_SAMPLE(SCOPE, STAGE, SAMPLE, FRAC,
     & PARAMETER, CURVATUREVALUE, CURVATURESUM)
      CHARACTER*(*) SCOPE, STAGE
      INTEGER TRACE_LENTRIM, TRACE_NEXTSEQ
      INTEGER LS, LT, SEQ, SAMPLE
      REAL FRAC, PARAMETER, CURVATUREVALUE, CURVATURESUM
      LOGICAL LOPEN
      CHARACTER*128 CSCOPE
      CHARACTER*64 CSTAGE
C
      INQUIRE(UNIT=51, OPENED=LOPEN)
      IF(.NOT.LOPEN) RETURN
      CALL TRACE_CLEAN(SCOPE, CSCOPE)
      CALL TRACE_CLEAN(STAGE, CSTAGE)
      LS = TRACE_LENTRIM(CSCOPE)
      LT = TRACE_LENTRIM(CSTAGE)
      IF(LS.LE.0) LS = 1
      IF(LT.LE.0) LT = 1
C
      SEQ = TRACE_NEXTSEQ()
      WRITE(51,1000) SEQ, CSCOPE(1:LS), CSTAGE(1:LT), SAMPLE,
     &               FRAC, PARAMETER, CURVATUREVALUE, CURVATURESUM
 1000 FORMAT('{"sequence":',I12,',"runtime":"fortran",'
     & '"kind":"pangen_le_sample","scope":"',A,
     & '","name":null,"data":{"stage":"',A,
     & '","sample":',I6,',"frac":',1PE24.16,
     & ',"parameter":',1PE24.16,
     & ',"curvatureValue":',1PE24.16,
     & ',"curvatureSum":',1PE24.16,
     & '},"values":null,"tags":null,"timestampUtc":null}')
      RETURN
      END


      SUBROUTINE TRACE_PANGEN_ITERATION(SCOPE, ITER, DMAX, RLX)
      CHARACTER*(*) SCOPE
      INTEGER TRACE_LENTRIM, TRACE_NEXTSEQ
      INTEGER LS, SEQ, ITER
      REAL DMAX, RLX
      LOGICAL LOPEN
      CHARACTER*128 CSCOPE
C
      INQUIRE(UNIT=51, OPENED=LOPEN)
      IF(.NOT.LOPEN) RETURN
      CALL TRACE_CLEAN(SCOPE, CSCOPE)
      LS = TRACE_LENTRIM(CSCOPE)
      IF(LS.LE.0) LS = 1
C
      SEQ = TRACE_NEXTSEQ()
      WRITE(51,1000) SEQ, CSCOPE(1:LS), ITER, DMAX, RLX
 1000 FORMAT('{"sequence":',I12,',"runtime":"fortran",'
     & '"kind":"pangen_iteration","scope":"',A,
     & '","name":null,"data":{"iteration":',I6,
     & ',"dmax":',1PE24.16,',"rlx":',1PE24.16,
     & '},"values":null,"tags":null,"timestampUtc":null}')
      RETURN
      END


      SUBROUTINE TRACE_PANGEN_NEWTON_ROW(SCOPE, ITER, INDEX, ARCLENGTH,
     & LOWER, DIAGONAL, UPPER, RESIDUAL)
      CHARACTER*(*) SCOPE
      INTEGER TRACE_LENTRIM, TRACE_NEXTSEQ
      INTEGER LS, SEQ, ITER, INDEX
      REAL ARCLENGTH, LOWER, DIAGONAL, UPPER, RESIDUAL
      LOGICAL LOPEN
      CHARACTER*128 CSCOPE
C
      INQUIRE(UNIT=51, OPENED=LOPEN)
      IF(.NOT.LOPEN) RETURN
      CALL TRACE_CLEAN(SCOPE, CSCOPE)
      LS = TRACE_LENTRIM(CSCOPE)
      IF(LS.LE.0) LS = 1
C
      SEQ = TRACE_NEXTSEQ()
      WRITE(51,1000) SEQ, CSCOPE(1:LS), ITER, INDEX, ARCLENGTH,
     &               LOWER, DIAGONAL, UPPER, RESIDUAL
 1000 FORMAT('{"sequence":',I12,',"runtime":"fortran",'
     & '"kind":"pangen_newton_row","scope":"',A,
     & '","name":null,"data":{"iteration":',I6,
     & ',"index":',I6,',"arcLength":',1PE24.16,
     & ',"lower":',1PE24.16,',"diagonal":',1PE24.16,
     & ',"upper":',1PE24.16,',"residual":',1PE24.16,
     & '},"values":null,"tags":null,"timestampUtc":null}')
      RETURN
      END


      SUBROUTINE TRACE_PANGEN_NEWTON_DELTA(SCOPE, ITER, INDEX, DELTA)
      CHARACTER*(*) SCOPE
      INTEGER TRACE_LENTRIM, TRACE_NEXTSEQ
      INTEGER LS, SEQ, ITER, INDEX
      REAL DELTA
      LOGICAL LOPEN
      CHARACTER*128 CSCOPE
C
      INQUIRE(UNIT=51, OPENED=LOPEN)
      IF(.NOT.LOPEN) RETURN
      CALL TRACE_CLEAN(SCOPE, CSCOPE)
      LS = TRACE_LENTRIM(CSCOPE)
      IF(LS.LE.0) LS = 1
C
      SEQ = TRACE_NEXTSEQ()
      WRITE(51,1000) SEQ, CSCOPE(1:LS), ITER, INDEX, DELTA
 1000 FORMAT('{"sequence":',I12,',"runtime":"fortran",'
     & '"kind":"pangen_newton_delta","scope":"',A,
     & '","name":null,"data":{"iteration":',I6,
     & ',"index":',I6,',"delta":',1PE24.16,
     & '},"values":null,"tags":null,"timestampUtc":null}')
      RETURN
      END


      SUBROUTINE TRACE_PANGEN_NEWTON_STATE(SCOPE, ITER, INDEX,
     & ARCLENGTH, DSM, DSP, CV1, CV2, CV3, CVS1, CVS2, CVS3,
     & CAVM, CAVMS1, CAVMS2, CAVP, CAVPS2, CAVPS3,
     & FM, FP, REZ, LOWER, DIAGONAL, UPPER, RESIDUAL)
      CHARACTER*(*) SCOPE
      INTEGER TRACE_LENTRIM, TRACE_NEXTSEQ
      INTEGER LS, SEQ, ITER, INDEX
      REAL ARCLENGTH, DSM, DSP, CV1, CV2, CV3, CVS1, CVS2, CVS3
      REAL CAVM, CAVMS1, CAVMS2, CAVP, CAVPS2, CAVPS3
      REAL FM, FP, REZ, LOWER, DIAGONAL, UPPER, RESIDUAL
      LOGICAL LOPEN
      CHARACTER*128 CSCOPE
C
      INQUIRE(UNIT=51, OPENED=LOPEN)
      IF(.NOT.LOPEN) RETURN
      CALL TRACE_CLEAN(SCOPE, CSCOPE)
      LS = TRACE_LENTRIM(CSCOPE)
      IF(LS.LE.0) LS = 1
C
      SEQ = TRACE_NEXTSEQ()
      WRITE(51,1000) SEQ, CSCOPE(1:LS), ITER, INDEX, ARCLENGTH,
     &               DSM, DSP, CV1, CV2, CV3, CVS1, CVS2, CVS3,
     &               CAVM, CAVMS1, CAVMS2, CAVP, CAVPS2, CAVPS3,
     &               FM, FP, REZ, LOWER, DIAGONAL, UPPER, RESIDUAL
 1000 FORMAT('{"sequence":',I12,',"runtime":"fortran",'
     & '"kind":"pangen_newton_state","scope":"',A,
     & '","name":null,"data":{"iteration":',I6,
     & ',"index":',I6,',"arcLength":',1PE24.16,
     & ',"dsm":',1PE24.16,',"dsp":',1PE24.16,
     & ',"cv1":',1PE24.16,',"cv2":',1PE24.16,
     & ',"cv3":',1PE24.16,',"cvs1":',1PE24.16,
     & ',"cvs2":',1PE24.16,',"cvs3":',1PE24.16,
     & ',"cavm":',1PE24.16,',"cavmS1":',1PE24.16,
     & ',"cavmS2":',1PE24.16,',"cavp":',1PE24.16,
     & ',"cavpS2":',1PE24.16,',"cavpS3":',1PE24.16,
     & ',"fm":',1PE24.16,',"fp":',1PE24.16,
     & ',"rez":',1PE24.16,',"lower":',1PE24.16,
     & ',"diagonal":',1PE24.16,',"upper":',1PE24.16,
     & ',"residual":',1PE24.16,
     & '},"values":null,"tags":null,"timestampUtc":null}')
      RETURN
      END


      SUBROUTINE TRACE_PANGEN_RELAXATION_STEP(SCOPE, ITER, INDEX,
     & DSVAL, DDSVAL, DSRAT, RLXBEFORE, RLXAFTER,
     & DMAXBEFORE, DMAXAFTER)
      CHARACTER*(*) SCOPE
      INTEGER TRACE_LENTRIM, TRACE_NEXTSEQ
      INTEGER LS, SEQ, ITER, INDEX
      REAL DSVAL, DDSVAL, DSRAT, RLXBEFORE, RLXAFTER
      REAL DMAXBEFORE, DMAXAFTER
      LOGICAL LOPEN
      CHARACTER*128 CSCOPE
C
      INQUIRE(UNIT=51, OPENED=LOPEN)
      IF(.NOT.LOPEN) RETURN
      CALL TRACE_CLEAN(SCOPE, CSCOPE)
      LS = TRACE_LENTRIM(CSCOPE)
      IF(LS.LE.0) LS = 1
C
      SEQ = TRACE_NEXTSEQ()
      WRITE(51,1000) SEQ, CSCOPE(1:LS), ITER, INDEX, DSVAL,
     &               DDSVAL, DSRAT, RLXBEFORE, RLXAFTER,
     &               DMAXBEFORE, DMAXAFTER
 1000 FORMAT('{"sequence":',I12,',"runtime":"fortran",'
     & '"kind":"pangen_relaxation_step","scope":"',A,
     & '","name":null,"data":{"iteration":',I6,
     & ',"index":',I6,',"ds":',1PE24.16,
     & ',"dds":',1PE24.16,',"dsrat":',1PE24.16,
     & ',"rlxBefore":',1PE24.16,',"rlxAfter":',1PE24.16,
     & ',"dmaxBefore":',1PE24.16,',"dmaxAfter":',1PE24.16,
     & '},"values":null,"tags":null,"timestampUtc":null}')
      RETURN
      END


      SUBROUTINE TRACE_PANGEN_SNEW_NODE(SCOPE, STAGE, ITER, INDEX,
     &                                  VALUE)
      CHARACTER*(*) SCOPE, STAGE
      INTEGER TRACE_LENTRIM, TRACE_NEXTSEQ
      INTEGER LS, LT, SEQ, ITER, INDEX
      REAL VALUE
      LOGICAL LOPEN
      CHARACTER*128 CSCOPE
      CHARACTER*64 CSTAGE
C
      INQUIRE(UNIT=51, OPENED=LOPEN)
      IF(.NOT.LOPEN) RETURN
      CALL TRACE_CLEAN(SCOPE, CSCOPE)
      CALL TRACE_CLEAN(STAGE, CSTAGE)
      LS = TRACE_LENTRIM(CSCOPE)
      LT = TRACE_LENTRIM(CSTAGE)
      IF(LS.LE.0) LS = 1
      IF(LT.LE.0) LT = 1
C
      SEQ = TRACE_NEXTSEQ()
      WRITE(51,1000) SEQ, CSCOPE(1:LS), CSTAGE(1:LT), ITER,
     &               INDEX, VALUE
 1000 FORMAT('{"sequence":',I12,',"runtime":"fortran",'
     & '"kind":"pangen_snew_node","scope":"',A,
     & '","name":null,"data":{"stage":"',A,
     & '","iteration":',I6,',"index":',I6,
     & ',"value":',1PE24.16,
     & '},"values":null,"tags":null,"timestampUtc":null}')
      RETURN
      END


      SUBROUTINE TRACE_WAKE_PANEL_STATE(SCOPE, INDEX, FIELDINDEX,
     & XVAL, YVAL, PSIX, PSIY, MAGVAL, APAN,
     & CURNX, CURNY, NXVAL, NYVAL)
      CHARACTER*(*) SCOPE
      INTEGER TRACE_LENTRIM, TRACE_NEXTSEQ
      INTEGER LS, SEQ, INDEX, FIELDINDEX
      REAL XVAL, YVAL, PSIX, PSIY, MAGVAL, APAN
      REAL CURNX, CURNY, NXVAL, NYVAL
      LOGICAL LOPEN
      CHARACTER*128 CSCOPE
C
      INQUIRE(UNIT=51, OPENED=LOPEN)
      IF(.NOT.LOPEN) RETURN
      CALL TRACE_CLEAN(SCOPE, CSCOPE)
      LS = TRACE_LENTRIM(CSCOPE)
      IF(LS.LE.0) LS = 1
C
      SEQ = TRACE_NEXTSEQ()
      WRITE(51,1000) SEQ, CSCOPE(1:LS), INDEX, FIELDINDEX,
     &               XVAL, YVAL, PSIX, PSIY, MAGVAL, APAN,
     &               CURNX, CURNY, NXVAL, NYVAL
 1000 FORMAT('{"sequence":',I12,',"runtime":"fortran",'
     & '"kind":"wake_panel_state","scope":"',A,
     & '","name":null,"data":{"index":',I6,
     & ',"fieldIndex":',I6,
     & ',"x":',1PE24.16,',"y":',1PE24.16,
     & ',"psiX":',1PE24.16,',"psiY":',1PE24.16,
     & ',"magnitude":',1PE24.16,
     & ',"usedFallback":0,'
     & '"panelAngle":',1PE24.16,
     & ',"currentNormalX":',1PE24.16,
     & ',"currentNormalY":',1PE24.16,
     & ',"nextNormalX":',1PE24.16,
     & ',"nextNormalY":',1PE24.16,
     & '},"values":null,"tags":null,"timestampUtc":null}')
      RETURN
      END


      SUBROUTINE TRACE_PANEL_NODE(SCOPE, INDEX, XVAL, YVAL,
     &                            XPVAL, YPVAL,
     &                            NXVAL, NYVAL, APAN)
      CHARACTER*(*) SCOPE
      INTEGER TRACE_LENTRIM, TRACE_NEXTSEQ
      INTEGER LS, SEQ, INDEX
      REAL XVAL, YVAL, XPVAL, YPVAL, NXVAL, NYVAL, APAN
      LOGICAL LOPEN
      CHARACTER*128 CSCOPE
C
      INQUIRE(UNIT=51, OPENED=LOPEN)
      IF(.NOT.LOPEN) RETURN
      CALL TRACE_CLEAN(SCOPE, CSCOPE)
      LS = TRACE_LENTRIM(CSCOPE)
      IF(LS.LE.0) LS = 1
C
      SEQ = TRACE_NEXTSEQ()
      WRITE(51,1000) SEQ, CSCOPE(1:LS), INDEX,
     &               XVAL, YVAL, XPVAL, YPVAL, NXVAL, NYVAL, APAN
 1000 FORMAT('{"sequence":',I12,',"runtime":"fortran",'
     & '"kind":"panel_node","scope":"',A,
     & '","name":null,"data":{"index":',I6,
     & ',"x":',1PE24.16,',"y":',1PE24.16,
     & ',"xp":',1PE24.16,',"yp":',1PE24.16,
     & ',"nx":',1PE24.16,',"ny":',1PE24.16,
     & ',"panelAngle":',1PE24.16,
     & '},"values":null,"tags":null,"timestampUtc":null}')
      RETURN
      END


      SUBROUTINE TRACE_PSWLIN_FIELD(SCOPE, FIELDINDEX, FIELDWAKEINDEX,
     &                              XVAL, YVAL, NXVAL, NYVAL)
      CHARACTER*(*) SCOPE
      INTEGER TRACE_LENTRIM, TRACE_NEXTSEQ
      INTEGER LS, SEQ, FIELDINDEX, FIELDWAKEINDEX
      REAL XVAL, YVAL, NXVAL, NYVAL
      LOGICAL LOPEN
      CHARACTER*128 CSCOPE
C
      INQUIRE(UNIT=51, OPENED=LOPEN)
      IF(.NOT.LOPEN) RETURN
      CALL TRACE_CLEAN(SCOPE, CSCOPE)
      LS = TRACE_LENTRIM(CSCOPE)
      IF(LS.LE.0) LS = 1
C
      SEQ = TRACE_NEXTSEQ()
      WRITE(51,1000) SEQ, CSCOPE(1:LS), FIELDINDEX, FIELDWAKEINDEX,
     &               XVAL, YVAL, NXVAL, NYVAL
 1000 FORMAT('{"sequence":',I12,',"runtime":"fortran",'
     & '"kind":"pswlin_field","scope":"',A,
     & '","name":null,"data":{"fieldIndex":',I6,
     & ',"fieldWakeIndex":',I6,
     & ',"fieldX":',1PE24.16,',"fieldY":',1PE24.16,
     & ',"fieldNormalX":',1PE24.16,',"fieldNormalY":',1PE24.16,
     & ',"precision":"Single"},"values":null,"tags":null,'
     & '"timestampUtc":null}')
      RETURN
      END


      SUBROUTINE TRACE_PSWLIN_GEOMETRY(SCOPE, FIELDINDEX,
     & FIELDWAKEINDEX, WAKESEGMENT,
     & XJO, YJO, XJP, YJP,
     & DXPANEL, DYPANEL, DSO, DSIO,
     & SX, SY, RX1, RY1, RX2, RY2)
      CHARACTER*(*) SCOPE
      INTEGER TRACE_LENTRIM, TRACE_NEXTSEQ
      INTEGER LS, SEQ, FIELDINDEX, FIELDWAKEINDEX, WAKESEGMENT
      REAL XJO, YJO, XJP, YJP, DXPANEL, DYPANEL, DSO, DSIO
      REAL SX, SY, RX1, RY1, RX2, RY2
      LOGICAL LOPEN
      CHARACTER*128 CSCOPE
C
      INQUIRE(UNIT=51, OPENED=LOPEN)
      IF(.NOT.LOPEN) RETURN
      CALL TRACE_CLEAN(SCOPE, CSCOPE)
      LS = TRACE_LENTRIM(CSCOPE)
      IF(LS.LE.0) LS = 1
C
      SEQ = TRACE_NEXTSEQ()
      WRITE(51,1000) SEQ, CSCOPE(1:LS), FIELDINDEX,
     & FIELDWAKEINDEX, WAKESEGMENT,
     & XJO, YJO, XJP, YJP, DXPANEL, DYPANEL, DSO, DSIO,
     & SX, SY, RX1, RY1, RX2, RY2
 1000 FORMAT('{"sequence":',I12,',"runtime":"fortran",'
     & '"kind":"pswlin_geometry","scope":"',A,
     & '","name":null,"data":{"fieldIndex":',I6,
     & ',"fieldWakeIndex":',I6,',"wakeSegment":',I6,
     & ',"precision":"Single",'
     & '"xJo":',1PE24.16,',"yJo":',1PE24.16,
     & ',"xJp":',1PE24.16,',"yJp":',1PE24.16,
     & ',"dxPanel":',1PE24.16,',"dyPanel":',1PE24.16,
     & ',"dso":',1PE24.16,',"dsio":',1PE24.16,
     & ',"sx":',1PE24.16,',"sy":',1PE24.16,
     & ',"rx1":',1PE24.16,',"ry1":',1PE24.16,
     & ',"rx2":',1PE24.16,',"ry2":',1PE24.16,
     & '},"values":null,"tags":null,"timestampUtc":null}')
      RETURN
      END


      SUBROUTINE TRACE_PSWLIN_PDX0_TERMS(SCOPE, FIELDINDEX,
     & FIELDWAKEINDEX, WAKESEGMENT, HALF,
     & PDX0TERM1, PDX0TERM2, PDX0TERM3,
     & PDX0ACCUM1, PDX0ACCUM2, PDX0NUMERATOR, PDX0)
      CHARACTER*(*) SCOPE
      INTEGER TRACE_LENTRIM, TRACE_NEXTSEQ
      INTEGER LS, SEQ, FIELDINDEX, FIELDWAKEINDEX, WAKESEGMENT, HALF
      REAL PDX0TERM1, PDX0TERM2, PDX0TERM3
      REAL PDX0ACCUM1, PDX0ACCUM2, PDX0NUMERATOR, PDX0
      LOGICAL LOPEN
      CHARACTER*128 CSCOPE
C
      INQUIRE(UNIT=51, OPENED=LOPEN)
      IF(.NOT.LOPEN) RETURN
      CALL TRACE_CLEAN(SCOPE, CSCOPE)
      LS = TRACE_LENTRIM(CSCOPE)
      IF(LS.LE.0) LS = 1
C
      SEQ = TRACE_NEXTSEQ()
      WRITE(51,1000) SEQ, CSCOPE(1:LS), FIELDINDEX,
     & FIELDWAKEINDEX, WAKESEGMENT, HALF,
     & PDX0TERM1, PDX0TERM2, PDX0TERM3,
     & PDX0ACCUM1, PDX0ACCUM2, PDX0NUMERATOR, PDX0
 1000 FORMAT('{"sequence":',I12,',"runtime":"fortran",'
     & '"kind":"pswlin_pdx0_terms","scope":"',A,
     & '","name":null,"data":{"fieldIndex":',I6,
     & ',"fieldWakeIndex":',I6,',"wakeSegment":',I6,
     & ',"half":',I4,',"precision":"Single",'
     & '"pdx0Term1":',1PE24.16,',"pdx0Term2":',1PE24.16,
     & ',"pdx0Term3":',1PE24.16,',"pdx0Accum1":',1PE24.16,
     & ',"pdx0Accum2":',1PE24.16,
     & ',"pdx0Numerator":',1PE24.16,',"pdx0":',1PE24.16,
     & '},"values":null,"tags":null,"timestampUtc":null}')
      RETURN
      END


      SUBROUTINE TRACE_PSWLIN_PDX1_TERMS(SCOPE, FIELDINDEX,
     & FIELDWAKEINDEX, WAKESEGMENT, HALF,
     & PDX1TERM1, PDX1TERM2, PDX1TERM3,
     & PDX1ACCUM1, PDX1ACCUM2, PDX1NUMERATOR, PDX1)
      CHARACTER*(*) SCOPE
      INTEGER TRACE_LENTRIM, TRACE_NEXTSEQ
      INTEGER LS, SEQ, FIELDINDEX, FIELDWAKEINDEX, WAKESEGMENT, HALF
      REAL PDX1TERM1, PDX1TERM2, PDX1TERM3
      REAL PDX1ACCUM1, PDX1ACCUM2, PDX1NUMERATOR, PDX1
      LOGICAL LOPEN
      CHARACTER*128 CSCOPE
      CHARACTER*8 PDX1TERM1BITS, PDX1TERM2BITS, PDX1TERM3BITS
      CHARACTER*8 PDX1ACCUM1BITS, PDX1ACCUM2BITS
      CHARACTER*8 PDX1NUMERATORBITS, PDX1BITS
C
      INQUIRE(UNIT=51, OPENED=LOPEN)
      IF(.NOT.LOPEN) RETURN
      CALL TRACE_CLEAN(SCOPE, CSCOPE)
      LS = TRACE_LENTRIM(CSCOPE)
      IF(LS.LE.0) LS = 1
C
      CALL TRACE_REALHEX(PDX1TERM1, PDX1TERM1BITS)
      CALL TRACE_REALHEX(PDX1TERM2, PDX1TERM2BITS)
      CALL TRACE_REALHEX(PDX1TERM3, PDX1TERM3BITS)
      CALL TRACE_REALHEX(PDX1ACCUM1, PDX1ACCUM1BITS)
      CALL TRACE_REALHEX(PDX1ACCUM2, PDX1ACCUM2BITS)
      CALL TRACE_REALHEX(PDX1NUMERATOR, PDX1NUMERATORBITS)
      CALL TRACE_REALHEX(PDX1, PDX1BITS)
C
      SEQ = TRACE_NEXTSEQ()
      WRITE(51,1000) SEQ, CSCOPE(1:LS), FIELDINDEX,
     & FIELDWAKEINDEX, WAKESEGMENT, HALF,
     & PDX1TERM1, PDX1TERM2, PDX1TERM3,
     & PDX1ACCUM1, PDX1ACCUM2, PDX1NUMERATOR, PDX1,
     & PDX1TERM1BITS, PDX1TERM2BITS, PDX1TERM3BITS,
     & PDX1ACCUM1BITS, PDX1ACCUM2BITS,
     & PDX1NUMERATORBITS, PDX1BITS
 1000 FORMAT('{"sequence":',I12,',"runtime":"fortran",'
     & '"kind":"pswlin_pdx1_terms","scope":"',A,
     & '","name":null,"data":{"fieldIndex":',I6,
     & ',"fieldWakeIndex":',I6,',"wakeSegment":',I6,
     & ',"half":',I4,',"precision":"Single",'
     & '"pdx1Term1":',1PE24.16,',"pdx1Term2":',1PE24.16,
     & ',"pdx1Term3":',1PE24.16,',"pdx1Accum1":',1PE24.16,
     & ',"pdx1Accum2":',1PE24.16,
     & ',"pdx1Numerator":',1PE24.16,',"pdx1":',1PE24.16,
     & '},"dataBits":{"pdx1Term1":{"f32":"0x',A,
     & '"},"pdx1Term2":{"f32":"0x',A,
     & '"},"pdx1Term3":{"f32":"0x',A,
     & '"},"pdx1Accum1":{"f32":"0x',A,
     & '"},"pdx1Accum2":{"f32":"0x',A,
     & '"},"pdx1Numerator":{"f32":"0x',A,
     & '"},"pdx1":{"f32":"0x',A,
     & '"}',
     & '},"values":null,"tags":null,"timestampUtc":null}')
      RETURN
      END


      SUBROUTINE TRACE_PSWLIN_PDX2_TERMS(SCOPE, FIELDINDEX,
     & FIELDWAKEINDEX, WAKESEGMENT, HALF,
     & PDX2TERM1, PDX2TERM2, PDX2TERM3,
     & PDX2ACCUM1, PDX2ACCUM2, PDX2NUMERATOR, PDX2)
      CHARACTER*(*) SCOPE
      INTEGER TRACE_LENTRIM, TRACE_NEXTSEQ
      INTEGER LS, SEQ, FIELDINDEX, FIELDWAKEINDEX, WAKESEGMENT, HALF
      REAL PDX2TERM1, PDX2TERM2, PDX2TERM3
      REAL PDX2ACCUM1, PDX2ACCUM2, PDX2NUMERATOR, PDX2
      LOGICAL LOPEN
      CHARACTER*128 CSCOPE
      CHARACTER*8 PDX2TERM1BITS, PDX2TERM2BITS, PDX2TERM3BITS
      CHARACTER*8 PDX2ACCUM1BITS, PDX2ACCUM2BITS
      CHARACTER*8 PDX2NUMERATORBITS, PDX2BITS
C
      INQUIRE(UNIT=51, OPENED=LOPEN)
      IF(.NOT.LOPEN) RETURN
      CALL TRACE_CLEAN(SCOPE, CSCOPE)
      LS = TRACE_LENTRIM(CSCOPE)
      IF(LS.LE.0) LS = 1
C
      CALL TRACE_REALHEX(PDX2TERM1, PDX2TERM1BITS)
      CALL TRACE_REALHEX(PDX2TERM2, PDX2TERM2BITS)
      CALL TRACE_REALHEX(PDX2TERM3, PDX2TERM3BITS)
      CALL TRACE_REALHEX(PDX2ACCUM1, PDX2ACCUM1BITS)
      CALL TRACE_REALHEX(PDX2ACCUM2, PDX2ACCUM2BITS)
      CALL TRACE_REALHEX(PDX2NUMERATOR, PDX2NUMERATORBITS)
      CALL TRACE_REALHEX(PDX2, PDX2BITS)
C
      SEQ = TRACE_NEXTSEQ()
      WRITE(51,1000) SEQ, CSCOPE(1:LS), FIELDINDEX,
     & FIELDWAKEINDEX, WAKESEGMENT, HALF,
     & PDX2TERM1, PDX2TERM2, PDX2TERM3,
     & PDX2ACCUM1, PDX2ACCUM2, PDX2NUMERATOR, PDX2,
     & PDX2TERM1BITS, PDX2TERM2BITS, PDX2TERM3BITS,
     & PDX2ACCUM1BITS, PDX2ACCUM2BITS,
     & PDX2NUMERATORBITS, PDX2BITS
 1000 FORMAT('{"sequence":',I12,',"runtime":"fortran",'
     & '"kind":"pswlin_pdx2_terms","scope":"',A,
     & '","name":null,"data":{"fieldIndex":',I6,
     & ',"fieldWakeIndex":',I6,',"wakeSegment":',I6,
     & ',"half":',I4,',"precision":"Single",'
     & '"pdx2Term1":',1PE24.16,',"pdx2Term2":',1PE24.16,
     & ',"pdx2Term3":',1PE24.16,',"pdx2Accum1":',1PE24.16,
     & ',"pdx2Accum2":',1PE24.16,
     & ',"pdx2Numerator":',1PE24.16,',"pdx2":',1PE24.16,
     & '},"dataBits":{"pdx2Term1":{"f32":"0x',A,
     & '"},"pdx2Term2":{"f32":"0x',A,
     & '"},"pdx2Term3":{"f32":"0x',A,
     & '"},"pdx2Accum1":{"f32":"0x',A,
     & '"},"pdx2Accum2":{"f32":"0x',A,
     & '"},"pdx2Numerator":{"f32":"0x',A,
     & '"},"pdx2":{"f32":"0x',A,
     & '"}',
     & '},"values":null,"tags":null,"timestampUtc":null}')
      RETURN
      END


      SUBROUTINE TRACE_PSWLIN_HALF_TERMS(SCOPE, FIELDINDEX,
     & FIELDWAKEINDEX, WAKESEGMENT, HALF, X0,
     & PSUMTERM1, PSUMTERM2, PSUMTERM3, PSUMACCUM, PSUM,
     & PDIFTERM1, PDIFTERM2, PDIFTERM3, PDIFTERM4,
     & PDIFBASE, PDIFACCUM, PDIFNUMERATOR, PDIF)
      CHARACTER*(*) SCOPE
      INTEGER TRACE_LENTRIM, TRACE_NEXTSEQ
      INTEGER LS, SEQ, FIELDINDEX, FIELDWAKEINDEX, WAKESEGMENT, HALF
      REAL X0
      REAL PSUMTERM1, PSUMTERM2, PSUMTERM3, PSUMACCUM, PSUM
      REAL PDIFTERM1, PDIFTERM2, PDIFTERM3, PDIFTERM4
      REAL PDIFBASE, PDIFACCUM, PDIFNUMERATOR, PDIF
      LOGICAL LOPEN
      CHARACTER*128 CSCOPE
      CHARACTER*8 X0BITS
C
      INQUIRE(UNIT=51, OPENED=LOPEN)
      IF(.NOT.LOPEN) RETURN
      CALL TRACE_CLEAN(SCOPE, CSCOPE)
      LS = TRACE_LENTRIM(CSCOPE)
      IF(LS.LE.0) LS = 1
C
      CALL TRACE_REALHEX(X0, X0BITS)
C
      SEQ = TRACE_NEXTSEQ()
      WRITE(51,1000) SEQ, CSCOPE(1:LS),
     & FIELDINDEX, FIELDWAKEINDEX, WAKESEGMENT, HALF, X0,
     & PSUMTERM1, PSUMTERM2, PSUMTERM3, PSUMACCUM, PSUM,
     & PDIFTERM1, PDIFTERM2, PDIFTERM3, PDIFTERM4,
     & PDIFBASE, PDIFACCUM, PDIFNUMERATOR, PDIF, X0BITS
 1000 FORMAT('{"sequence":',I12,',"runtime":"fortran",'
     & '"kind":"pswlin_half_terms","scope":"',A,
     & '","name":null,"data":{"fieldIndex":',I6,
     & ',"fieldWakeIndex":',I6,',"wakeSegment":',I6,
     & ',"half":',I4,',"precision":"Single",'
     & '"x0":',1PE24.16,',"psumTerm1":',1PE24.16,
     & ',"psumTerm2":',1PE24.16,',"psumTerm3":',1PE24.16,
     & ',"psumAccum":',1PE24.16,',"psum":',1PE24.16,
     & ',"pdifTerm1":',1PE24.16,',"pdifTerm2":',1PE24.16,
     & ',"pdifTerm3":',1PE24.16,',"pdifTerm4":',1PE24.16,
     & ',"pdifBase":',1PE24.16,',"pdifAccum":',1PE24.16,
     & ',"pdifNumerator":',1PE24.16,',"pdif":',1PE24.16,
     & '},"dataBits":{"x0":{"f32":"0x',A,'"',
     & '}},"values":null,"tags":null,"timestampUtc":null}')
      RETURN
      END


      SUBROUTINE TRACE_PSWLIN_NI_TERMS(SCOPE, FIELDINDEX,
     & FIELDWAKEINDEX, WAKESEGMENT, HALF,
     & XSUM, XHALF,
     & PSLEADRAW, PSLEADSCALED, PSTERM1, PSTERM2, PSTERM3,
     & PSACCUM12, PSNI,
     & PDLEADRAW, PDLEADSCALED, PDTERM1, PDTERM2, PDTERM3,
     & PDACCUM12, PDNI)
      CHARACTER*(*) SCOPE
      INTEGER TRACE_LENTRIM, TRACE_NEXTSEQ
      INTEGER LS, SEQ, FIELDINDEX, FIELDWAKEINDEX, WAKESEGMENT, HALF
      REAL XSUM, XHALF
      REAL PSLEADRAW, PSLEADSCALED, PSTERM1, PSTERM2, PSTERM3
      REAL PSACCUM12, PSNI
      REAL PDLEADRAW, PDLEADSCALED, PDTERM1, PDTERM2, PDTERM3
      REAL PDACCUM12, PDNI
      LOGICAL LOPEN
      CHARACTER*128 CSCOPE
C
      INQUIRE(UNIT=51, OPENED=LOPEN)
      IF(.NOT.LOPEN) RETURN
      CALL TRACE_CLEAN(SCOPE, CSCOPE)
      LS = TRACE_LENTRIM(CSCOPE)
      IF(LS.LE.0) LS = 1
C
      SEQ = TRACE_NEXTSEQ()
      WRITE(51,1000) SEQ, CSCOPE(1:LS),
     & FIELDINDEX, FIELDWAKEINDEX, WAKESEGMENT, HALF,
     & XSUM, XHALF,
     & PSLEADRAW, PSLEADSCALED, PSTERM1, PSTERM2, PSTERM3,
     & PSACCUM12, PSNI,
     & PDLEADRAW, PDLEADSCALED, PDTERM1, PDTERM2, PDTERM3,
     & PDACCUM12, PDNI
 1000 FORMAT('{"sequence":',I12,',"runtime":"fortran",'
     & '"kind":"pswlin_ni_terms","scope":"',A,
     & '","name":null,"data":{"fieldIndex":',I6,
     & ',"fieldWakeIndex":',I6,',"wakeSegment":',I6,
     & ',"half":',I4,',"precision":"Single",'
     & '"xSum":',1PE24.16,',"xHalf":',1PE24.16,
     & ',"psLeadRaw":',1PE24.16,
     & ',"psLeadScaled":',1PE24.16,
     & ',"psTerm1":',1PE24.16,',"psTerm2":',1PE24.16,
     & ',"psTerm3":',1PE24.16,',"psAccum12":',1PE24.16,
     & ',"psni":',1PE24.16,
     & ',"pdLeadRaw":',1PE24.16,
     & ',"pdLeadScaled":',1PE24.16,
     & ',"pdTerm1":',1PE24.16,',"pdTerm2":',1PE24.16,
     & ',"pdTerm3":',1PE24.16,',"pdAccum12":',1PE24.16,
     & ',"pdni":',1PE24.16,
     & '},"values":null,"tags":null,"timestampUtc":null}')
      RETURN
      END


      SUBROUTINE TRACE_PSWLIN_SEGMENT(SCOPE, FIELDINDEX, FIELDWAKEINDEX,
     & HALF, JM, JO, JP, JQ,
     & X1, X2, YY, SGN, APAN,
     & X1I, X2I, YYI,
     & RS0, RS1, RS2, G0, G1, G2, T0, T1, T2,
     & DSO, DSIO, DSM, DSIM, DSP, DSIP, DXINV,
     & SSUM, SDIF, PSUM, PDIF,
     & PSX0, PSX1, PSX2, PSYY,
     & PDX0, PDX1, PDX2, PDYY,
     & PSNI, PDNI,
     & DZJM, DZJO, DZJP, DZJQ,
     & DQJM, DQJO, DQJP, DQJQ)
      CHARACTER*(*) SCOPE
      INTEGER TRACE_LENTRIM, TRACE_NEXTSEQ
      INTEGER LS, SEQ, FIELDINDEX, FIELDWAKEINDEX, HALF
      INTEGER JM, JO, JP, JQ
      REAL X1, X2, YY, SGN, APAN, X1I, X2I, YYI
      REAL RS0, RS1, RS2, G0, G1, G2, T0, T1, T2
      REAL DSO, DSIO, DSM, DSIM, DSP, DSIP, DXINV
      REAL SSUM, SDIF, PSUM, PDIF
      REAL PSX0, PSX1, PSX2, PSYY
      REAL PDX0, PDX1, PDX2, PDYY
      REAL PSNI, PDNI
      REAL DZJM, DZJO, DZJP, DZJQ
      REAL DQJM, DQJO, DQJP, DQJQ
      LOGICAL LOPEN
      CHARACTER*128 CSCOPE
      CHARACTER*8 X1BITS, X2BITS, YYBITS
      CHARACTER*8 X1IBITS, X2IBITS, YYIBITS
      CHARACTER*8 DXINVBITS, PSUMBITS, PDIFBITS
      CHARACTER*8 PSX0BITS, PSX1BITS, PSX2BITS, PSYYBITS
      CHARACTER*8 PDX0BITS, PDX1BITS, PDX2BITS, PDYYBITS
      CHARACTER*8 PSNIBITS, PDNIBITS
C
      INQUIRE(UNIT=51, OPENED=LOPEN)
      IF(.NOT.LOPEN) RETURN
      CALL TRACE_CLEAN(SCOPE, CSCOPE)
      LS = TRACE_LENTRIM(CSCOPE)
      IF(LS.LE.0) LS = 1
C
      CALL TRACE_REALHEX(X1, X1BITS)
      CALL TRACE_REALHEX(X2, X2BITS)
      CALL TRACE_REALHEX(YY, YYBITS)
      CALL TRACE_REALHEX(X1I, X1IBITS)
      CALL TRACE_REALHEX(X2I, X2IBITS)
      CALL TRACE_REALHEX(YYI, YYIBITS)
      CALL TRACE_REALHEX(DXINV, DXINVBITS)
      CALL TRACE_REALHEX(PSUM, PSUMBITS)
      CALL TRACE_REALHEX(PDIF, PDIFBITS)
      CALL TRACE_REALHEX(PSX0, PSX0BITS)
      CALL TRACE_REALHEX(PSX1, PSX1BITS)
      CALL TRACE_REALHEX(PSX2, PSX2BITS)
      CALL TRACE_REALHEX(PSYY, PSYYBITS)
      CALL TRACE_REALHEX(PDX0, PDX0BITS)
      CALL TRACE_REALHEX(PDX1, PDX1BITS)
      CALL TRACE_REALHEX(PDX2, PDX2BITS)
      CALL TRACE_REALHEX(PDYY, PDYYBITS)
      CALL TRACE_REALHEX(PSNI, PSNIBITS)
      CALL TRACE_REALHEX(PDNI, PDNIBITS)
C
      SEQ = TRACE_NEXTSEQ()
      WRITE(51,1000) SEQ, CSCOPE(1:LS),
     & FIELDINDEX, FIELDWAKEINDEX, JO, HALF, JM, JO, JP, JQ,
     & X1, X2, YY, SGN, APAN, X1I, X2I, YYI,
     & RS0, RS1, RS2, G0, G1, G2, T0, T1, T2,
     & DSO, DSIO, DSM, DSIM, DSP, DSIP, DXINV,
     & SSUM, SDIF, PSUM, PDIF,
     & PSX0, PSX1, PSX2, PSYY,
     & PDX0, PDX1, PDX2, PDYY,
     & PSNI, PDNI,
     & DZJM, DZJO, DZJP, DZJQ,
     & DQJM, DQJO, DQJP, DQJQ,
     & X1BITS, X2BITS, YYBITS,
     & X1IBITS, X2IBITS, YYIBITS,
     & DXINVBITS, PSUMBITS, PDIFBITS,
     & PSX0BITS, PSX1BITS, PSX2BITS, PSYYBITS,
     & PDX0BITS, PDX1BITS, PDX2BITS, PDYYBITS,
     & PSNIBITS, PDNIBITS
 1000 FORMAT('{"sequence":',I12,',"runtime":"fortran",'
     & '"kind":"pswlin_segment","scope":"',A,
     & '","name":null,"data":{"fieldIndex":',I6,
     & ',"fieldWakeIndex":',I6,',"wakeSegment":',I6,
     & ',"half":',I4,',"jm":',I6,',"jo":',I6,
     & ',"jp":',I6,',"jq":',I6,
     & ',"precision":"Single",'
     & '"x1":',1PE24.16,',"x2":',1PE24.16,
     & ',"yy":',1PE24.16,',"sgn":',1PE24.16,
     & ',"panelAngle":',1PE24.16,
     & ',"x1i":',1PE24.16,',"x2i":',1PE24.16,
     & ',"yyi":',1PE24.16,
     & ',"rs0":',1PE24.16,',"rs1":',1PE24.16,
     & ',"rs2":',1PE24.16,',"g0":',1PE24.16,
     & ',"g1":',1PE24.16,',"g2":',1PE24.16,
     & ',"t0":',1PE24.16,',"t1":',1PE24.16,
     & ',"t2":',1PE24.16,
     & ',"dso":',1PE24.16,',"dsio":',1PE24.16,
     & ',"dsm":',1PE24.16,',"dsim":',1PE24.16,
     & ',"dsp":',1PE24.16,',"dsip":',1PE24.16,
     & ',"dxInv":',1PE24.16,
     & ',"ssum":',1PE24.16,',"sdif":',1PE24.16,
     & ',"psum":',1PE24.16,',"pdif":',1PE24.16,
     & ',"psx0":',1PE24.16,',"psx1":',1PE24.16,
     & ',"psx2":',1PE24.16,',"psyy":',1PE24.16,
     & ',"pdx0":',1PE24.16,',"pdx1":',1PE24.16,
     & ',"pdx2":',1PE24.16,',"pdyy":',1PE24.16,
     & ',"psni":',1PE24.16,',"pdni":',1PE24.16,
     & ',"dzJm":',1PE24.16,',"dzJo":',1PE24.16,
     & ',"dzJp":',1PE24.16,',"dzJq":',1PE24.16,
     & ',"dqJm":',1PE24.16,',"dqJo":',1PE24.16,
     & ',"dqJp":',1PE24.16,',"dqJq":',1PE24.16,
     & '},"dataBits":{"x1":{"f32":"0x',A,
     & '"},"x2":{"f32":"0x',A,
     & '"},"yy":{"f32":"0x',A,
     & '"},"x1i":{"f32":"0x',A,
     & '"},"x2i":{"f32":"0x',A,
     & '"},"yyi":{"f32":"0x',A,
     & '"},"dxInv":{"f32":"0x',A,
     & '"},"psum":{"f32":"0x',A,
     & '"},"pdif":{"f32":"0x',A,
     & '"},"psx0":{"f32":"0x',A,
     & '"},"psx1":{"f32":"0x',A,
     & '"},"psx2":{"f32":"0x',A,
     & '"},"psyy":{"f32":"0x',A,
     & '"},"pdx0":{"f32":"0x',A,
     & '"},"pdx1":{"f32":"0x',A,
     & '"},"pdx2":{"f32":"0x',A,
     & '"},"pdyy":{"f32":"0x',A,
     & '"},"psni":{"f32":"0x',A,
     & '"},"pdni":{"f32":"0x',A,
     & '"}',
     & '},"values":null,"tags":null,"timestampUtc":null}')
      RETURN
      END


      SUBROUTINE TRACE_WAKE_SOURCE_ACCUM(SCOPE, FIELDINDEX,
     & FIELDWAKEINDEX, WAKESEGMENT, HALF, TARGETINDEX,
     & QUANTITY, TERM, DELTA, TOTAL)
      CHARACTER*(*) SCOPE, QUANTITY, TERM
      INTEGER TRACE_LENTRIM, TRACE_NEXTSEQ
      INTEGER LS, SEQ, FIELDINDEX, FIELDWAKEINDEX, WAKESEGMENT, HALF
      INTEGER TARGETINDEX
      REAL DELTA, TOTAL
      LOGICAL LOPEN
      CHARACTER*128 CSCOPE, CQUANTITY, CTERM
C
      INQUIRE(UNIT=51, OPENED=LOPEN)
      IF(.NOT.LOPEN) RETURN
      CALL TRACE_CLEAN(SCOPE, CSCOPE)
      CALL TRACE_CLEAN(QUANTITY, CQUANTITY)
      CALL TRACE_CLEAN(TERM, CTERM)
      LS = TRACE_LENTRIM(CSCOPE)
      IF(LS.LE.0) LS = 1
C
      SEQ = TRACE_NEXTSEQ()
      WRITE(51,1000) SEQ, CSCOPE(1:LS), FIELDINDEX, FIELDWAKEINDEX,
     &               WAKESEGMENT, HALF, TARGETINDEX,
     &               CQUANTITY(1:TRACE_LENTRIM(CQUANTITY)),
     &               CTERM(1:TRACE_LENTRIM(CTERM)), DELTA, TOTAL
 1000 FORMAT('{"sequence":',I12,',"runtime":"fortran",'
     & '"kind":"wake_source_accum","scope":"',A,
     & '","name":null,"data":{"fieldIndex":',I6,
     & ',"fieldWakeIndex":',I6,',"wakeSegment":',I6,
     & ',"half":',I4,',"targetIndex":',I6,
     & ',"precision":"Single","quantity":"',A,
     & '","term":"',A,'","delta":',1PE24.16,
     & ',"total":',1PE24.16,
     & '},"values":null,"tags":null,"timestampUtc":null}')
      RETURN
      END


      SUBROUTINE TRACE_PSWLIN_RECURRENCE(SCOPE, FIELDINDEX,
     & FIELDWAKEINDEX, WAKESEGMENT, HALF,
     & DZJOLEFT, DZJORIGHT, DZJOINNER, DZJO,
     & DQJOLEFT, DQJORIGHT, DQJOINNER, DQJO, QOPI)
      CHARACTER*(*) SCOPE
      INTEGER TRACE_LENTRIM, TRACE_NEXTSEQ
      INTEGER LS, SEQ, FIELDINDEX, FIELDWAKEINDEX, WAKESEGMENT, HALF
      REAL DZJOLEFT, DZJORIGHT, DZJOINNER, DZJO
      REAL DQJOLEFT, DQJORIGHT, DQJOINNER, DQJO, QOPI
      LOGICAL LOPEN
      CHARACTER*128 CSCOPE
C
      INQUIRE(UNIT=51, OPENED=LOPEN)
      IF(.NOT.LOPEN) RETURN
      CALL TRACE_CLEAN(SCOPE, CSCOPE)
      LS = TRACE_LENTRIM(CSCOPE)
      IF(LS.LE.0) LS = 1
C
      SEQ = TRACE_NEXTSEQ()
      WRITE(51,1000) SEQ, CSCOPE(1:LS), FIELDINDEX, FIELDWAKEINDEX,
     &               WAKESEGMENT, HALF, DZJOLEFT, DZJORIGHT,
     &               DZJOINNER, DZJO, DQJOLEFT, DQJORIGHT,
     &               DQJOINNER, DQJO, QOPI
 1000 FORMAT('{"sequence":',I12,',"runtime":"fortran",'
     & '"kind":"pswlin_recurrence","scope":"',A,
     & '","name":null,"data":{"fieldIndex":',I6,
     & ',"fieldWakeIndex":',I6,',"wakeSegment":',I6,
     & ',"half":',I4,',"precision":"Single",'
     & '"dzJoLeft":',1PE24.16,',"dzJoRight":',1PE24.16,
     & ',"dzJoInner":',1PE24.16,',"dzJo":',1PE24.16,
     & ','
     & '"dqJoLeft":',1PE24.16,',"dqJoRight":',1PE24.16,
     & ',"dqJoInner":',1PE24.16,',"dqJo":',1PE24.16,
     & ',"qopi":',1PE24.16,
     & '},"values":null,"tags":null,"timestampUtc":null}')
      RETURN
      END


      SUBROUTINE TRACE_WAKE_SOURCE_ENTRY(SCOPE, FIELDINDEX,
     & FIELDWAKEINDEX, INDEX, DZVAL, DQVAL)
      CHARACTER*(*) SCOPE
      INTEGER TRACE_LENTRIM, TRACE_NEXTSEQ
      INTEGER LS, SEQ, FIELDINDEX, FIELDWAKEINDEX, INDEX
      REAL DZVAL, DQVAL
      LOGICAL LOPEN
      CHARACTER*128 CSCOPE
C
      INQUIRE(UNIT=51, OPENED=LOPEN)
      IF(.NOT.LOPEN) RETURN
      CALL TRACE_CLEAN(SCOPE, CSCOPE)
      LS = TRACE_LENTRIM(CSCOPE)
      IF(LS.LE.0) LS = 1
C
      SEQ = TRACE_NEXTSEQ()
      WRITE(51,1000) SEQ, CSCOPE(1:LS), FIELDINDEX, FIELDWAKEINDEX,
     &               INDEX, DZVAL, DQVAL
 1000 FORMAT('{"sequence":',I12,',"runtime":"fortran",'
     & '"kind":"wake_source_entry","scope":"',A,
     & '","name":null,"data":{"fieldIndex":',I6,
     & ',"fieldWakeIndex":',I6,',"index":',I6,
     & ',"precision":"Single","dzdm":',1PE24.16,
     & ',"dqdm":',1PE24.16,
     & '},"values":null,"tags":null,"timestampUtc":null}')
      RETURN
      END


      SUBROUTINE TRACE_PSILIN_SOURCE_HALF_TERMS(SCOPE, FIELDINDEX,
     & PANELINDEX, HALF, X0,
     & PSUMTERM1, PSUMTERM2, PSUMTERM3, PSUMACCUM, PSUM,
     & PDIFTERM1, PDIFTERM2, PDIFTERM3, PDIFTERM4,
     & PDIFACCUM1, PDIFACCUM2, PDIFNUMERATOR, PDIF)
      CHARACTER*(*) SCOPE
      INTEGER TRACE_LENTRIM, TRACE_NEXTSEQ
      INTEGER LS, SEQ, FIELDINDEX, PANELINDEX, HALF
      REAL X0, PSUMTERM1, PSUMTERM2, PSUMTERM3, PSUMACCUM, PSUM
      REAL PDIFTERM1, PDIFTERM2, PDIFTERM3, PDIFTERM4
      REAL PDIFACCUM1, PDIFACCUM2, PDIFNUMERATOR, PDIF
      LOGICAL LOPEN
      CHARACTER*128 CSCOPE
C
      INQUIRE(UNIT=51, OPENED=LOPEN)
      IF(.NOT.LOPEN) RETURN
      CALL TRACE_CLEAN(SCOPE, CSCOPE)
      LS = TRACE_LENTRIM(CSCOPE)
      IF(LS.LE.0) LS = 1
C
      SEQ = TRACE_NEXTSEQ()
      WRITE(51,1000) SEQ, CSCOPE(1:LS),
     & FIELDINDEX, PANELINDEX, HALF, X0,
     & PSUMTERM1, PSUMTERM2, PSUMTERM3, PSUMACCUM, PSUM,
     & PDIFTERM1, PDIFTERM2, PDIFTERM3, PDIFTERM4,
     & PDIFACCUM1, PDIFACCUM2, PDIFNUMERATOR, PDIF
 1000 FORMAT('{"sequence":',I12,',"runtime":"fortran",'
     & '"kind":"psilin_source_half_terms","scope":"',A,
     & '","name":null,"data":{"fieldIndex":',I6,
     & ',"panelIndex":',I6,',"half":',I4,
     & ',"precision":"Single",'
     & '"x0":',1PE24.16,',"psumTerm1":',1PE24.16,
     & ',"psumTerm2":',1PE24.16,',"psumTerm3":',1PE24.16,
     & ',"psumAccum":',1PE24.16,',"psum":',1PE24.16,
     & ',"pdifTerm1":',1PE24.16,',"pdifTerm2":',1PE24.16,
     & ',"pdifTerm3":',1PE24.16,',"pdifTerm4":',1PE24.16,
     & ',"pdifAccum1":',1PE24.16,',"pdifAccum2":',1PE24.16,
     & ',"pdifNumerator":',1PE24.16,',"pdif":',1PE24.16,
     & '},"values":null,"tags":null,"timestampUtc":null}')
      RETURN
      END


      SUBROUTINE TRACE_PSILIN_SOURCE_DZ_TERMS(SCOPE, FIELDINDEX,
     & PANELINDEX, HALF,
     & DZJMTERM1, DZJMTERM2, DZJMINNER,
     & DZJOTERM1, DZJOTERM2, DZJOINNER,
     & DZJPTERM1, DZJPTERM2, DZJPINNER,
     & DZJQTERM1, DZJQTERM2, DZJQINNER)
      CHARACTER*(*) SCOPE
      INTEGER TRACE_LENTRIM, TRACE_NEXTSEQ
      INTEGER LS, SEQ, FIELDINDEX, PANELINDEX, HALF
      REAL DZJMTERM1, DZJMTERM2, DZJMINNER
      REAL DZJOTERM1, DZJOTERM2, DZJOINNER
      REAL DZJPTERM1, DZJPTERM2, DZJPINNER
      REAL DZJQTERM1, DZJQTERM2, DZJQINNER
      LOGICAL LOPEN
      CHARACTER*128 CSCOPE
C
      INQUIRE(UNIT=51, OPENED=LOPEN)
      IF(.NOT.LOPEN) RETURN
      CALL TRACE_CLEAN(SCOPE, CSCOPE)
      LS = TRACE_LENTRIM(CSCOPE)
      IF(LS.LE.0) LS = 1
C
      SEQ = TRACE_NEXTSEQ()
      WRITE(51,1000) SEQ, CSCOPE(1:LS),
     & FIELDINDEX, PANELINDEX, HALF,
     & DZJMTERM1, DZJMTERM2, DZJMINNER,
     & DZJOTERM1, DZJOTERM2, DZJOINNER,
     & DZJPTERM1, DZJPTERM2, DZJPINNER,
     & DZJQTERM1, DZJQTERM2, DZJQINNER
 1000 FORMAT('{"sequence":',I12,',"runtime":"fortran",'
     & '"kind":"psilin_source_dz_terms","scope":"',A,
     & '","name":null,"data":{"fieldIndex":',I6,
     & ',"panelIndex":',I6,',"half":',I4,
     & ',"precision":"Single",'
     & '"dzJmTerm1":',1PE24.16,',"dzJmTerm2":',1PE24.16,
     & ',"dzJmInner":',1PE24.16,
     & ',"dzJoTerm1":',1PE24.16,',"dzJoTerm2":',1PE24.16,
     & ',"dzJoInner":',1PE24.16,
     & ',"dzJpTerm1":',1PE24.16,',"dzJpTerm2":',1PE24.16,
     & ',"dzJpInner":',1PE24.16,
     & ',"dzJqTerm1":',1PE24.16,',"dzJqTerm2":',1PE24.16,
     & ',"dzJqInner":',1PE24.16,
     & '},"values":null,"tags":null,"timestampUtc":null}')
      RETURN
      END


      SUBROUTINE TRACE_PSILIN_SOURCE_DQ_TERMS(SCOPE, FIELDINDEX,
     & PANELINDEX, HALF,
     & DQJMTERM1, DQJMTERM2, DQJMINNER,
     & DQJOTERM1, DQJOTERM2, DQJOINNER,
     & DQJPTERM1, DQJPTERM2, DQJPINNER,
     & DQJQTERM1, DQJQTERM2, DQJQINNER)
      CHARACTER*(*) SCOPE
      INTEGER TRACE_LENTRIM, TRACE_NEXTSEQ
      INTEGER LS, SEQ, FIELDINDEX, PANELINDEX, HALF
      REAL DQJMTERM1, DQJMTERM2, DQJMINNER
      REAL DQJOTERM1, DQJOTERM2, DQJOINNER
      REAL DQJPTERM1, DQJPTERM2, DQJPINNER
      REAL DQJQTERM1, DQJQTERM2, DQJQINNER
      LOGICAL LOPEN
      CHARACTER*128 CSCOPE
C
      INQUIRE(UNIT=51, OPENED=LOPEN)
      IF(.NOT.LOPEN) RETURN
      CALL TRACE_CLEAN(SCOPE, CSCOPE)
      LS = TRACE_LENTRIM(CSCOPE)
      IF(LS.LE.0) LS = 1
C
      SEQ = TRACE_NEXTSEQ()
      WRITE(51,1000) SEQ, CSCOPE(1:LS),
     & FIELDINDEX, PANELINDEX, HALF,
     & DQJMTERM1, DQJMTERM2, DQJMINNER,
     & DQJOTERM1, DQJOTERM2, DQJOINNER,
     & DQJPTERM1, DQJPTERM2, DQJPINNER,
     & DQJQTERM1, DQJQTERM2, DQJQINNER
 1000 FORMAT('{"sequence":',I12,',"runtime":"fortran",'
     & '"kind":"psilin_source_dq_terms","scope":"',A,
     & '","name":null,"data":{"fieldIndex":',I6,
     & ',"panelIndex":',I6,',"half":',I4,
     & ',"precision":"Single",'
     & '"dqJmTerm1":',1PE24.16,',"dqJmTerm2":',1PE24.16,
     & ',"dqJmInner":',1PE24.16,
     & ',"dqJoTerm1":',1PE24.16,',"dqJoTerm2":',1PE24.16,
     & ',"dqJoInner":',1PE24.16,
     & ',"dqJpTerm1":',1PE24.16,',"dqJpTerm2":',1PE24.16,
     & ',"dqJpInner":',1PE24.16,
     & ',"dqJqTerm1":',1PE24.16,',"dqJqTerm2":',1PE24.16,
     & ',"dqJqInner":',1PE24.16,
     & '},"values":null,"tags":null,"timestampUtc":null}')
      RETURN
      END


      SUBROUTINE TRACE_PSILIN_SOURCE_SEGMENT(SCOPE, FIELDINDEX,
     & PANELINDEX, HALF, JM, JO, JP, JQ,
     & X0, X1, X2, YY, APAN, X1I, X2I, YYI,
     & RS0, RS1, RS2, G0, G1, G2, T0, T1, T2,
     & DSO, DSIO, DSM, DSIM, DSP, DSIP, DXINV,
     & SOURCETERMLEFT, SOURCETERMRIGHT,
     & SSUM, SDIF, PSUM, PDIF,
     & PSX0, PSX1, PSX2, PSYY,
     & PDX0TERM1, PDX0TERM2, PDX0NUMERATOR, PDX0,
     & PDX1TERM1, PDX1TERM2, PDX1NUMERATOR, PDX1,
     & PDX2TERM1, PDX2TERM2, PDX2NUMERATOR, PDX2,
     & PDYYTERM1, PDYYTAILLINEAR, PDYYTAILANGULAR,
     & PDYYTERM2, PDYYNUMERATOR, PDYY,
     & PSNITERM1, PSNITERM2, PSNITERM3, PSNI,
     & PDNITERM1, PDNITERM2, PDNITERM3, PDNI,
     & DZJM, DZJO, DZJP, DZJQ,
     & DQJM, DQJO, DQJP, DQJQ)
      CHARACTER*(*) SCOPE
      INTEGER TRACE_LENTRIM, TRACE_NEXTSEQ
      INTEGER LS, SEQ, FIELDINDEX, PANELINDEX, HALF
      INTEGER JM, JO, JP, JQ
      REAL X0, X1, X2, YY, APAN, X1I, X2I, YYI
      REAL RS0, RS1, RS2, G0, G1, G2, T0, T1, T2
      REAL DSO, DSIO, DSM, DSIM, DSP, DSIP, DXINV
      REAL SOURCETERMLEFT, SOURCETERMRIGHT
      REAL SSUM, SDIF, PSUM, PDIF
      REAL PSX0, PSX1, PSX2, PSYY
      REAL PDX0TERM1, PDX0TERM2, PDX0NUMERATOR, PDX0
      REAL PDX1TERM1, PDX1TERM2, PDX1NUMERATOR, PDX1
      REAL PDX2TERM1, PDX2TERM2, PDX2NUMERATOR, PDX2
      REAL PDYYTERM1, PDYYTAILLINEAR, PDYYTAILANGULAR
      REAL PDYYTERM2, PDYYNUMERATOR, PDYY
      REAL PSNITERM1, PSNITERM2, PSNITERM3, PSNI
      REAL PDNITERM1, PDNITERM2, PDNITERM3, PDNI
      REAL DZJM, DZJO, DZJP, DZJQ
      REAL DQJM, DQJO, DQJP, DQJQ
      LOGICAL LOPEN
      CHARACTER*128 CSCOPE
C
      INQUIRE(UNIT=51, OPENED=LOPEN)
      IF(.NOT.LOPEN) RETURN
      CALL TRACE_CLEAN(SCOPE, CSCOPE)
      LS = TRACE_LENTRIM(CSCOPE)
      IF(LS.LE.0) LS = 1
C
      SEQ = TRACE_NEXTSEQ()
      WRITE(51,1000) SEQ, CSCOPE(1:LS),
     & FIELDINDEX, PANELINDEX, HALF, JM, JO, JP, JQ,
     & X0, X1, X2, YY, APAN, X1I, X2I, YYI,
     & RS0, RS1, RS2, G0, G1, G2, T0, T1, T2,
     & DSO, DSIO, DSM, DSIM, DSP, DSIP, DXINV,
     & SOURCETERMLEFT, SOURCETERMRIGHT,
     & SSUM, SDIF, PSUM, PDIF,
     & PSX0, PSX1, PSX2, PSYY,
     & PDX0TERM1, PDX0TERM2, PDX0NUMERATOR, PDX0,
     & PDX1TERM1, PDX1TERM2, PDX1NUMERATOR, PDX1,
     & PDX2TERM1, PDX2TERM2, PDX2NUMERATOR, PDX2,
     & PDYYTERM1, PDYYTAILLINEAR, PDYYTAILANGULAR,
     & PDYYTERM2, PDYYNUMERATOR, PDYY,
     & PSNITERM1, PSNITERM2, PSNITERM3, PSNI,
     & PDNITERM1, PDNITERM2, PDNITERM3, PDNI,
     & DZJM, DZJO, DZJP, DZJQ,
     & DQJM, DQJO, DQJP, DQJQ
 1000 FORMAT('{"sequence":',I12,',"runtime":"fortran",'
     & '"kind":"psilin_source_segment","scope":"',A,
     & '","name":null,"data":{"fieldIndex":',I6,
     & ',"panelIndex":',I6,',"half":',I4,
     & ',"jm":',I6,',"jo":',I6,',"jp":',I6,',"jq":',I6,
     & ',"precision":"Single",'
     & '"x0":',1PE24.16,',"x1":',1PE24.16,
     & ',"x2":',1PE24.16,
     & ',"yy":',1PE24.16,',"panelAngle":',1PE24.16,
     & ',"x1i":',1PE24.16,',"x2i":',1PE24.16,
     & ',"yyi":',1PE24.16,
     & ',"rs0":',1PE24.16,',"rs1":',1PE24.16,
     & ',"rs2":',1PE24.16,',"g0":',1PE24.16,
     & ',"g1":',1PE24.16,',"g2":',1PE24.16,
     & ',"t0":',1PE24.16,',"t1":',1PE24.16,
     & ',"t2":',1PE24.16,
     & ',"dso":',1PE24.16,',"dsio":',1PE24.16,
     & ',"dsm":',1PE24.16,',"dsim":',1PE24.16,
     & ',"dsp":',1PE24.16,',"dsip":',1PE24.16,
     & ',"dxInv":',1PE24.16,
     & ',"sourceTermLeft":',1PE24.16,
     & ',"sourceTermRight":',1PE24.16,
     & ',"ssum":',1PE24.16,',"sdif":',1PE24.16,
     & ',"psum":',1PE24.16,',"pdif":',1PE24.16,
     & ',"psx0":',1PE24.16,',"psx1":',1PE24.16,
     & ',"psx2":',1PE24.16,',"psyy":',1PE24.16,
     & ',"pdx0Term1":',1PE24.16,',"pdx0Term2":',1PE24.16,
     & ',"pdx0Numerator":',1PE24.16,',"pdx0":',1PE24.16,
     & ',"pdx1Term1":',1PE24.16,',"pdx1Term2":',1PE24.16,
     & ',"pdx1Numerator":',1PE24.16,',"pdx1":',1PE24.16,
     & ',"pdx2Term1":',1PE24.16,',"pdx2Term2":',1PE24.16,
     & ',"pdx2Numerator":',1PE24.16,',"pdx2":',1PE24.16,
     & ',"pdyyTerm1":',1PE24.16,
     & ',"pdyyTailLinear":',1PE24.16,
     & ',"pdyyTailAngular":',1PE24.16,
     & ',"pdyyTerm2":',1PE24.16,
     & ',"pdyyNumerator":',1PE24.16,',"pdyy":',1PE24.16,
     & ',"psniTerm1":',1PE24.16,',"psniTerm2":',1PE24.16,
     & ',"psniTerm3":',1PE24.16,',"psni":',1PE24.16,
     & ',"pdniTerm1":',1PE24.16,',"pdniTerm2":',1PE24.16,
     & ',"pdniTerm3":',1PE24.16,',"pdni":',1PE24.16,
     & ',"dzJm":',1PE24.16,',"dzJo":',1PE24.16,
     & ',"dzJp":',1PE24.16,',"dzJq":',1PE24.16,
     & ',"dqJm":',1PE24.16,',"dqJo":',1PE24.16,
     & ',"dqJp":',1PE24.16,',"dqJq":',1PE24.16,
     & '},"values":null,"tags":null,"timestampUtc":null}')
      RETURN
      END


      SUBROUTINE TRACE_PSILIN_SOURCE_PDYY_WRITE(SCOPE, FIELDINDEX,
     & PANELINDEX, HALF,
     & X0, XEDGE, YY, T0, TEDGE, PSYY, DXINV,
     & PDYYWRITEDT, PDYYWRITEINNER,
     & PDYYWRITEHEAD, PDYYWRITETAIL, PDYYWRITESUM, PDYYWRITEVALUE,
     & PDYYTERM1, PDYYTERM2, PDYYNUMERATOR, PDYY)
      CHARACTER*(*) SCOPE
      INTEGER TRACE_LENTRIM, TRACE_NEXTSEQ
      INTEGER LS, SEQ, FIELDINDEX, PANELINDEX, HALF
      REAL X0, XEDGE, YY, T0, TEDGE, PSYY, DXINV
      REAL PDYYWRITEDT, PDYYWRITEINNER
      REAL PDYYWRITEHEAD, PDYYWRITETAIL, PDYYWRITESUM, PDYYWRITEVALUE
      REAL PDYYTERM1, PDYYTERM2, PDYYNUMERATOR, PDYY
      LOGICAL LOPEN
      CHARACTER*128 CSCOPE
C
      INQUIRE(UNIT=51, OPENED=LOPEN)
      IF(.NOT.LOPEN) RETURN
      CALL TRACE_CLEAN(SCOPE, CSCOPE)
      LS = TRACE_LENTRIM(CSCOPE)
      IF(LS.LE.0) LS = 1
C
      SEQ = TRACE_NEXTSEQ()
      WRITE(51,1000) SEQ, CSCOPE(1:LS),
     & FIELDINDEX, PANELINDEX, HALF,
     & X0, XEDGE, YY, T0, TEDGE, PSYY, DXINV,
     & PDYYWRITEDT, PDYYWRITEINNER,
     & PDYYWRITEHEAD, PDYYWRITETAIL, PDYYWRITESUM, PDYYWRITEVALUE,
     & PDYYTERM1, PDYYTERM2, PDYYNUMERATOR, PDYY
 1000 FORMAT('{"sequence":',I12,',"runtime":"fortran",'
     & '"kind":"psilin_source_pdyy_write","scope":"',A,
     & '","name":null,"data":{"fieldIndex":',I6,
     & ',"panelIndex":',I6,',"half":',I4,
     & ',"precision":"Single",'
     & '"x0":',1PE24.16,',"xEdge":',1PE24.16,
     & ',"yy":',1PE24.16,',"t0":',1PE24.16,
     & ',"tEdge":',1PE24.16,',"psyy":',1PE24.16,
     & ',"dxInv":',1PE24.16,
     & ',"pdyyWriteDt":',1PE24.16,
     & ',"pdyyWriteInner":',1PE24.16,
     & ',"pdyyWriteHead":',1PE24.16,
     & ',"pdyyWriteTail":',1PE24.16,
     & ',"pdyyWriteSum":',1PE24.16,
     & ',"pdyyWriteValue":',1PE24.16,
     & ',"pdyyTerm1":',1PE24.16,
     & ',"pdyyTerm2":',1PE24.16,
     & ',"pdyyNumerator":',1PE24.16,
     & ',"pdyy":',1PE24.16,
     & '},"values":null,"tags":null,"timestampUtc":null}')
      RETURN
      END


      SUBROUTINE TRACE_PSILIN_VORTEX_SEGMENT(SCOPE, FIELDINDEX,
     & PANELJO, PANELJP, X1, X2, YY, RS1, RS2, G1, G2, T1, T2,
     & DXINV,
     & PSIST1, PSIST2, PSIST3, PSIST4, PSIS,
     & PSIDT1, PSIDT2, PSIDT3, PSIDT4, PSIDT5, PSIDH, PSID,
     & PSX1, PSX2, PSYY,
     & PDXSUM, PDX1MUL, PDX1PAN, PDX1A1, PDX1A2, PDX1NUM,
     & PDX1, PDX2MUL, PDX2PAN, PDX2A1, PDX2A2, PDX2NUM,
     & PDX2, PDYY,
     & GAMMAJO, GAMMAJP, GSUM, GDIF, PSNI, PDNI, PSIDLT, PSNIDLT,
     & DZJO, DZJP, DQJO, DQJP)
      CHARACTER*(*) SCOPE
      INTEGER TRACE_LENTRIM, TRACE_NEXTSEQ
      INTEGER LS, SEQ, FIELDINDEX, PANELJO, PANELJP
      REAL X1, X2, YY, RS1, RS2, G1, G2, T1, T2
      REAL DXINV
      REAL PSIST1, PSIST2, PSIST3, PSIST4, PSIS
      REAL PSIDT1, PSIDT2, PSIDT3, PSIDT4, PSIDT5, PSIDH, PSID
      REAL PSX1, PSX2, PSYY
      REAL PDXSUM, PDX1MUL, PDX1PAN, PDX1A1, PDX1A2, PDX1NUM
      REAL PDX1, PDX2MUL, PDX2PAN, PDX2A1, PDX2A2, PDX2NUM
      REAL PDX2, PDYY
      REAL GAMMAJO, GAMMAJP, GSUM, GDIF, PSNI, PDNI, PSIDLT, PSNIDLT
      REAL DZJO, DZJP, DQJO, DQJP
      LOGICAL LOPEN
      CHARACTER*128 CSCOPE
C
      INQUIRE(UNIT=51, OPENED=LOPEN)
      IF(.NOT.LOPEN) RETURN
      CALL TRACE_CLEAN(SCOPE, CSCOPE)
      LS = TRACE_LENTRIM(CSCOPE)
      IF(LS.LE.0) LS = 1
C
      SEQ = TRACE_NEXTSEQ()
      WRITE(51,1000) SEQ, CSCOPE(1:LS),
     & FIELDINDEX, PANELJO, PANELJP,
     & X1, X2, YY, RS1, RS2, G1, G2, T1, T2,
     & DXINV,
     & PSIST1, PSIST2, PSIST3, PSIST4, PSIS,
     & PSIDT1, PSIDT2, PSIDT3, PSIDT4, PSIDT5, PSIDH, PSID,
     & PSX1, PSX2, PSYY,
     & PDXSUM, PDX1MUL, PDX1PAN, PDX1A1, PDX1A2, PDX1NUM,
     & PDX1, PDX2MUL, PDX2PAN, PDX2A1, PDX2A2, PDX2NUM,
     & PDX2, PDYY,
     & GAMMAJO, GAMMAJP, GSUM, GDIF, PSNI, PDNI, PSIDLT, PSNIDLT,
     & DZJO, DZJP, DQJO, DQJP
 1000 FORMAT('{"sequence":',I12,',"runtime":"fortran",'
     & '"kind":"psilin_vortex_segment","scope":"',A,
     & '","name":null,"data":{"fieldIndex":',I6,
     & ',"jo":',I6,',"jp":',I6,
     & ',"precision":"Single",'
     & '"x1":',1PE24.16,',"x2":',1PE24.16,
     & ',"yy":',1PE24.16,',"rs1":',1PE24.16,
     & ',"rs2":',1PE24.16,',"g1":',1PE24.16,
     & ',"g2":',1PE24.16,',"t1":',1PE24.16,
     & ',"t2":',1PE24.16,',"dxInv":',1PE24.16,
     & ',"psisTerm1":',1PE24.16,',"psisTerm2":',1PE24.16,
     & ',"psisTerm3":',1PE24.16,',"psisTerm4":',1PE24.16,
     & ',"psis":',1PE24.16,
     & ',"psidTerm1":',1PE24.16,',"psidTerm2":',1PE24.16,
     & ',"psidTerm3":',1PE24.16,',"psidTerm4":',1PE24.16,
     & ',"psidTerm5":',1PE24.16,',"psidHalfTerm":',1PE24.16,
     & ',"psid":',1PE24.16,
     & ',"psx1":',1PE24.16,',"psx2":',1PE24.16,
     & ',"psyy":',1PE24.16,',"pdxSum":',1PE24.16,
     & ',"pdx1Mul":',1PE24.16,',"pdx1PanelTerm":',1PE24.16,
     & ',"pdx1Accum1":',1PE24.16,',"pdx1Accum2":',1PE24.16,
     & ',"pdx1Numerator":',1PE24.16,',"pdx1":',1PE24.16,
     & ',"pdx2Mul":',1PE24.16,',"pdx2PanelTerm":',1PE24.16,
     & ',"pdx2Accum1":',1PE24.16,',"pdx2Accum2":',1PE24.16,
     & ',"pdx2Numerator":',1PE24.16,',"pdx2":',1PE24.16,
     & ',"pdyy":',1PE24.16,
     & ',"gammaJo":',1PE24.16,',"gammaJp":',1PE24.16,
     & ',"gsum":',1PE24.16,',"gdif":',1PE24.16,
     & ',"psni":',1PE24.16,',"pdni":',1PE24.16,
     & ',"psiDelta":',1PE24.16,',"psiNiDelta":',1PE24.16,
     & ',"dzJo":',1PE24.16,',"dzJp":',1PE24.16,
     & ',"dqJo":',1PE24.16,',"dqJp":',1PE24.16,
     & '},"values":null,"tags":null,"timestampUtc":null}')
      RETURN
      END


      SUBROUTINE TRACE_PSILIN_TE_CORRECTION(SCOPE, FIELDINDEX,
     & JO, JP, PSIG, PGAM, PSIGNI, PGAMNI, SIGTE, GAMTE,
     & SCS, SDS, DZJOTESIG, DZJPTESIG, DZJOTEGAM, DZJPTEGAM,
     & DQJOTESIGHALF, DQJOTESIGTERM,
     & DQJOTEGAMHALF, DQJOTEGAMTERM, DQTEINNER,
     & DQJOTE, DQJPTE)
      CHARACTER*(*) SCOPE
      INTEGER TRACE_LENTRIM, TRACE_NEXTSEQ
      INTEGER LS, SEQ, FIELDINDEX, JO, JP
      REAL PSIG, PGAM, PSIGNI, PGAMNI, SIGTE, GAMTE, SCS, SDS
      REAL DZJOTESIG, DZJPTESIG, DZJOTEGAM, DZJPTEGAM
      REAL DQJOTESIGHALF, DQJOTESIGTERM
      REAL DQJOTEGAMHALF, DQJOTEGAMTERM, DQTEINNER
      REAL DQJOTE, DQJPTE
      LOGICAL LOPEN
      CHARACTER*128 CSCOPE
C
      INQUIRE(UNIT=51, OPENED=LOPEN)
      IF(.NOT.LOPEN) RETURN
      CALL TRACE_CLEAN(SCOPE, CSCOPE)
      LS = TRACE_LENTRIM(CSCOPE)
      IF(LS.LE.0) LS = 1
C
      SEQ = TRACE_NEXTSEQ()
      WRITE(51,1000) SEQ, CSCOPE(1:LS), FIELDINDEX, JO, JP,
     & PSIG, PGAM, PSIGNI, PGAMNI, SIGTE, GAMTE, SCS, SDS,
     & DZJOTESIG, DZJPTESIG, DZJOTEGAM, DZJPTEGAM,
     & DQJOTESIGHALF, DQJOTESIGTERM,
     & DQJOTEGAMHALF, DQJOTEGAMTERM, DQTEINNER,
     & DQJOTE, DQJPTE
 1000 FORMAT('{"sequence":',I12,',"runtime":"fortran",'
     & '"kind":"psilin_te_correction","scope":"',A,
     & '","name":null,"data":{"fieldIndex":',I6,
     & ',"jo":',I6,',"jp":',I6,
     & ',"precision":"Single",'
     & '"psig":',1PE24.16,',"pgam":',1PE24.16,
     & ',"psigni":',1PE24.16,',"pgamni":',1PE24.16,
     & ',"sigte":',1PE24.16,',"gamte":',1PE24.16,
     & ',"scs":',1PE24.16,',"sds":',1PE24.16,
     & ',"dzJoTeSig":',1PE24.16,',"dzJpTeSig":',1PE24.16,
     & ',"dzJoTeGam":',1PE24.16,',"dzJpTeGam":',1PE24.16,
     & ',"dqJoTeSigHalf":',1PE24.16,',"dqJoTeSigTerm":',1PE24.16,
     & ',"dqJoTeGamHalf":',1PE24.16,',"dqJoTeGamTerm":',1PE24.16,
     & ',"dqTeInner":',1PE24.16,
     & ',"dqJoTe":',1PE24.16,',"dqJpTe":',1PE24.16,
     & '},"values":null,"tags":null,"timestampUtc":null}')
      RETURN
      END


      SUBROUTINE TRACE_PSILIN_TE_PGAM_TERMS(SCOPE, FIELDINDEX,
     & JO, JP, PGAMLEADPRODUCT1, PGAMLEADPRODUCT2, PGAMLEADPAIR,
     & PGAMBASE, PGAMDT, PGAMTAIL)
      CHARACTER*(*) SCOPE
      INTEGER TRACE_LENTRIM, TRACE_NEXTSEQ
      INTEGER LS, SEQ, FIELDINDEX, JO, JP
      REAL PGAMLEADPRODUCT1, PGAMLEADPRODUCT2, PGAMLEADPAIR
      REAL PGAMBASE, PGAMDT, PGAMTAIL
      LOGICAL LOPEN
      CHARACTER*128 CSCOPE
C
      INQUIRE(UNIT=51, OPENED=LOPEN)
      IF(.NOT.LOPEN) RETURN
      CALL TRACE_CLEAN(SCOPE, CSCOPE)
      LS = TRACE_LENTRIM(CSCOPE)
      IF(LS.LE.0) LS = 1
C
      SEQ = TRACE_NEXTSEQ()
      WRITE(51,1000) SEQ, CSCOPE(1:LS), FIELDINDEX, JO, JP,
     & PGAMLEADPRODUCT1, PGAMLEADPRODUCT2, PGAMLEADPAIR,
     & PGAMBASE, PGAMDT, PGAMTAIL
 1000 FORMAT('{"sequence":',I12,',"runtime":"fortran",'
     & '"kind":"psilin_te_pgam_terms","scope":"',A,
     & '","name":null,"data":{"fieldIndex":',I6,
     & ',"jo":',I6,',"jp":',I6,
     & ',"precision":"Single",'
     & '"pgamLeadProduct1":',1PE24.16,
     & ',"pgamLeadProduct2":',1PE24.16,
     & ',"pgamLeadPair":',1PE24.16,
     & ',"pgamBase":',1PE24.16,
     & ',"pgamDt":',1PE24.16,
     & ',"pgamTail":',1PE24.16,
     & '},"values":null,"tags":null,"timestampUtc":null}')
      RETURN
      END


      SUBROUTINE TRACE_WAKE_NODE(SCOPE, IW, XVAL, YVAL, NXVAL, NYVAL,
     &                           APAN)
      CHARACTER*(*) SCOPE
      INTEGER TRACE_LENTRIM, TRACE_NEXTSEQ
      INTEGER LS, SEQ, IW
      REAL XVAL, YVAL, NXVAL, NYVAL, APAN
      LOGICAL LOPEN
      CHARACTER*128 CSCOPE
C
      INQUIRE(UNIT=51, OPENED=LOPEN)
      IF(.NOT.LOPEN) RETURN
      CALL TRACE_CLEAN(SCOPE, CSCOPE)
      LS = TRACE_LENTRIM(CSCOPE)
      IF(LS.LE.0) LS = 1
C
      SEQ = TRACE_NEXTSEQ()
      WRITE(51,1000) SEQ, CSCOPE(1:LS), IW, XVAL, YVAL, NXVAL, NYVAL,
     &               APAN
 1000 FORMAT('{"sequence":',I12,',"runtime":"fortran",'
     & '"kind":"wake_node","scope":"',A,
     & '","name":null,"data":{"index":',I6,
     & ',"x":',1PE24.16,',"y":',1PE24.16,
     & ',"nx":',1PE24.16,',"ny":',1PE24.16,
     & ',"panelAngle":',1PE24.16,
     & '},"values":null,"tags":null,"timestampUtc":null}')
      RETURN
      END


      SUBROUTINE TRACE_WAKE_SPACING(SCOPE, INDEX, DISTANCE, DELTA,
     &                              FIRSTSPACING)
      CHARACTER*(*) SCOPE
      INTEGER TRACE_LENTRIM, TRACE_NEXTSEQ
      INTEGER LS, SEQ, INDEX
      REAL DISTANCE, DELTA, FIRSTSPACING
      LOGICAL LOPEN
      CHARACTER*128 CSCOPE
C
      INQUIRE(UNIT=51, OPENED=LOPEN)
      IF(.NOT.LOPEN) RETURN
      CALL TRACE_CLEAN(SCOPE, CSCOPE)
      LS = TRACE_LENTRIM(CSCOPE)
      IF(LS.LE.0) LS = 1
C
      SEQ = TRACE_NEXTSEQ()
      WRITE(51,1000) SEQ, CSCOPE(1:LS), INDEX,
     &               DISTANCE, DELTA, FIRSTSPACING
 1000 FORMAT('{"sequence":',I12,',"runtime":"fortran",'
     & '"kind":"wake_spacing","scope":"',A,
     & '","name":null,"data":{"index":',I6,
     & ',"distance":',1PE24.16,',"delta":',1PE24.16,
     & ',"firstSpacing":',1PE24.16,
     & '},"values":null,"tags":null,"timestampUtc":null}')
      RETURN
      END


      SUBROUTINE TRACE_WAKE_SPACING_INPUT(SCOPE, UPPERSTART, UPPEREND,
     & LOWERSTART, LOWEREND, UPPERDELTA, LOWERDELTA, DS1)
      CHARACTER*(*) SCOPE
      INTEGER TRACE_LENTRIM, TRACE_NEXTSEQ
      INTEGER LS, SEQ
      REAL UPPERSTART, UPPEREND, LOWERSTART, LOWEREND
      REAL UPPERDELTA, LOWERDELTA, DS1
      LOGICAL LOPEN
      CHARACTER*128 CSCOPE
C
      INQUIRE(UNIT=51, OPENED=LOPEN)
      IF(.NOT.LOPEN) RETURN
      CALL TRACE_CLEAN(SCOPE, CSCOPE)
      LS = TRACE_LENTRIM(CSCOPE)
      IF(LS.LE.0) LS = 1
C
      SEQ = TRACE_NEXTSEQ()
      WRITE(51,1000) SEQ, CSCOPE(1:LS),
     &               UPPERSTART, UPPEREND, LOWERSTART, LOWEREND,
     &               UPPERDELTA, LOWERDELTA, DS1
 1000 FORMAT('{"sequence":',I12,',"runtime":"fortran",'
     & '"kind":"wake_spacing_input","scope":"',A,
     & '","name":null,"data":{"upperStart":',1PE24.16,
     & ',"upperEnd":',1PE24.16,',"lowerStart":',1PE24.16,
     & ',"lowerEnd":',1PE24.16,',"upperDelta":',1PE24.16,
     & ',"lowerDelta":',1PE24.16,',"ds1":',1PE24.16,
     & '},"values":null,"tags":null,"timestampUtc":null}')
      RETURN
      END


      SUBROUTINE TRACE_MATRIX_ENTRY(SCOPE, NAME, ROW, COL, VALUE)
      CHARACTER*(*) SCOPE, NAME
      INTEGER TRACE_LENTRIM, TRACE_NEXTSEQ
      INTEGER LS, LN, SEQ, ROW, COL
      REAL VALUE
      LOGICAL LOPEN
      CHARACTER*128 CSCOPE, CNAME
C
      INQUIRE(UNIT=51, OPENED=LOPEN)
      IF(.NOT.LOPEN) RETURN
      CALL TRACE_CLEAN(SCOPE, CSCOPE)
      CALL TRACE_CLEAN(NAME, CNAME)
      LS = TRACE_LENTRIM(CSCOPE)
      LN = TRACE_LENTRIM(CNAME)
      IF(LS.LE.0) LS = 1
      IF(LN.LE.0) LN = 1
C
      SEQ = TRACE_NEXTSEQ()
      WRITE(51,1000) SEQ, CSCOPE(1:LS), CNAME(1:LN), ROW, COL, VALUE
 1000 FORMAT('{"sequence":',I12,',"runtime":"fortran",'
     & '"kind":"matrix_entry","scope":"',A,
     & '","name":null,"data":{"matrix":"',A,
     & '","row":',I6,',"col":',I6,
     & ',"value":',1PE24.16,
     & ',"precision":"SingleKernel"},"values":null,'
     & '"tags":null,"timestampUtc":null}')
      RETURN
      END


      SUBROUTINE TRACE_BASIS_ENTRY(SCOPE, NAME, INDEX, VALUE)
      CHARACTER*(*) SCOPE, NAME
      INTEGER TRACE_LENTRIM, TRACE_NEXTSEQ
      INTEGER LS, LN, SEQ, INDEX
      REAL VALUE
      LOGICAL LOPEN
      CHARACTER*128 CSCOPE, CNAME
C
      INQUIRE(UNIT=51, OPENED=LOPEN)
      IF(.NOT.LOPEN) RETURN
      CALL TRACE_CLEAN(SCOPE, CSCOPE)
      CALL TRACE_CLEAN(NAME, CNAME)
      LS = TRACE_LENTRIM(CSCOPE)
      LN = TRACE_LENTRIM(CNAME)
      IF(LS.LE.0) LS = 1
      IF(LN.LE.0) LN = 1
C
      SEQ = TRACE_NEXTSEQ()
      WRITE(51,1000) SEQ, CSCOPE(1:LS), CNAME(1:LN), INDEX, VALUE
 1000 FORMAT('{"sequence":',I12,',"runtime":"fortran",'
     & '"kind":"basis_entry","scope":"',A,
     & '","name":"',A,'","data":{"index":',I6,
     & ',"value":',1PE24.16,
     & ',"precision":"Single"},"values":null,'
     & '"tags":null,"timestampUtc":null}')
      RETURN
      END


      SUBROUTINE TRACE_PIVOT_ENTRY(SCOPE, NAME, INDEX, VALUE)
      CHARACTER*(*) SCOPE, NAME
      INTEGER TRACE_LENTRIM, TRACE_NEXTSEQ
      INTEGER LS, LN, SEQ, INDEX, VALUE
      LOGICAL LOPEN
      CHARACTER*128 CSCOPE, CNAME
C
      INQUIRE(UNIT=51, OPENED=LOPEN)
      IF(.NOT.LOPEN) RETURN
      CALL TRACE_CLEAN(SCOPE, CSCOPE)
      CALL TRACE_CLEAN(NAME, CNAME)
      LS = TRACE_LENTRIM(CSCOPE)
      LN = TRACE_LENTRIM(CNAME)
      IF(LS.LE.0) LS = 1
      IF(LN.LE.0) LN = 1
C
      SEQ = TRACE_NEXTSEQ()
      WRITE(51,1000) SEQ, CSCOPE(1:LS), CNAME(1:LN), INDEX, VALUE
 1000 FORMAT('{"sequence":',I12,',"runtime":"fortran",'
     & '"kind":"pivot_entry","scope":"',A,
     & '","name":null,"data":{"vector":"',A,
     & '","index":',I6,',"value":',I6,
     & ',"precision":"Single"},"values":null,'
     & '"tags":null,"timestampUtc":null}')
      RETURN
      END


      SUBROUTINE TRACE_LU_DECOMPOSE_TERM(SCOPE, CONTEXT, PHASE,
     & ROW, COL, INNERCOL, LEFTVALUE, RIGHTVALUE,
     & PRODUCT, SUMBEFORE, SUMAFTER)
      CHARACTER*(*) SCOPE, CONTEXT, PHASE
      INTEGER TRACE_LENTRIM, TRACE_NEXTSEQ
      INTEGER LS, LC, LP, SEQ, ROW, COL, INNERCOL
      REAL LEFTVALUE, RIGHTVALUE, PRODUCT, SUMBEFORE, SUMAFTER
      LOGICAL LOPEN
      CHARACTER*128 CSCOPE, CCONTEXT
      CHARACTER*32 CPHASE
C
      INQUIRE(UNIT=51, OPENED=LOPEN)
      IF(.NOT.LOPEN) RETURN
      CALL TRACE_CLEAN(SCOPE, CSCOPE)
      CALL TRACE_CLEAN(CONTEXT, CCONTEXT)
      CALL TRACE_CLEAN(PHASE, CPHASE)
      LS = TRACE_LENTRIM(CSCOPE)
      LC = TRACE_LENTRIM(CCONTEXT)
      LP = TRACE_LENTRIM(CPHASE)
      IF(LS.LE.0) LS = 1
      IF(LC.LE.0) LC = 1
      IF(LP.LE.0) LP = 1
C
      SEQ = TRACE_NEXTSEQ()
      WRITE(51,1000) SEQ, CSCOPE(1:LS), CCONTEXT(1:LC),
     &               CPHASE(1:LP), ROW, COL, INNERCOL,
     &               LEFTVALUE, RIGHTVALUE, PRODUCT, SUMBEFORE,
     &               SUMAFTER
 1000 FORMAT('{"sequence":',I12,',"runtime":"fortran",'
     & '"kind":"lu_decompose_term","scope":"',A,
     & '","name":null,"data":{"context":"',A,
     & '","phase":"',A,'","row":',I6,
     & ',"column":',I6,',"innerColumn":',I6,
     & ',"leftValue":',1PE24.16,',"rightValue":',1PE24.16,
     & ',"product":',1PE24.16,',"sumBefore":',1PE24.16,
     & ',"sumAfter":',1PE24.16,
     & ',"precision":"Single"},"values":null,'
     & '"tags":null,"timestampUtc":null}')
      RETURN
      END


      SUBROUTINE TRACE_LU_PIVOT(SCOPE, CONTEXT, COLUMN, PIVOTROW,
     &                          DIAGONAL, MAXSCALED)
      CHARACTER*(*) SCOPE, CONTEXT
      INTEGER TRACE_LENTRIM, TRACE_NEXTSEQ
      INTEGER LS, LC, SEQ, COLUMN, PIVOTROW
      REAL DIAGONAL, MAXSCALED
      LOGICAL LOPEN
      CHARACTER*128 CSCOPE, CCONTEXT
C
      INQUIRE(UNIT=51, OPENED=LOPEN)
      IF(.NOT.LOPEN) RETURN
      CALL TRACE_CLEAN(SCOPE, CSCOPE)
      CALL TRACE_CLEAN(CONTEXT, CCONTEXT)
      LS = TRACE_LENTRIM(CSCOPE)
      LC = TRACE_LENTRIM(CCONTEXT)
      IF(LS.LE.0) LS = 1
      IF(LC.LE.0) LC = 1
C
      SEQ = TRACE_NEXTSEQ()
      WRITE(51,1000) SEQ, CSCOPE(1:LS), CCONTEXT(1:LC), COLUMN,
     &               PIVOTROW, DIAGONAL, MAXSCALED
 1000 FORMAT('{"sequence":',I12,',"runtime":"fortran",'
     & '"kind":"lu_pivot","scope":"',A,
     & '","name":null,"data":{"context":"',A,
     & '","column":',I6,',"pivotRow":',I6,
     & ',"diagonal":',1PE24.16,',"maxScaled":',1PE24.16,
     & ',"precision":"Single"},"values":null,'
     & '"tags":null,"timestampUtc":null}')
      RETURN
      END


      SUBROUTINE TRACE_LU_BACKSUB_ROW(SCOPE, CONTEXT, PHASE,
     & ROW, PIVOTROW, II, SUMBEFORE, SUMAFTER, DIVISOR, SOLUTION)
      CHARACTER*(*) SCOPE, CONTEXT, PHASE
      INTEGER TRACE_LENTRIM, TRACE_NEXTSEQ
      INTEGER LS, LC, LP, SEQ, ROW, PIVOTROW, II
      REAL SUMBEFORE, SUMAFTER, DIVISOR, SOLUTION
      LOGICAL LOPEN
      CHARACTER*128 CSCOPE, CCONTEXT
      CHARACTER*32 CPHASE
C
      INQUIRE(UNIT=51, OPENED=LOPEN)
      IF(.NOT.LOPEN) RETURN
      CALL TRACE_CLEAN(SCOPE, CSCOPE)
      CALL TRACE_CLEAN(CONTEXT, CCONTEXT)
      CALL TRACE_CLEAN(PHASE, CPHASE)
      LS = TRACE_LENTRIM(CSCOPE)
      LC = TRACE_LENTRIM(CCONTEXT)
      LP = TRACE_LENTRIM(CPHASE)
      IF(LS.LE.0) LS = 1
      IF(LC.LE.0) LC = 1
      IF(LP.LE.0) LP = 1
C
      SEQ = TRACE_NEXTSEQ()
      WRITE(51,1000) SEQ, CSCOPE(1:LS), CCONTEXT(1:LC),
     &               CPHASE(1:LP), ROW, PIVOTROW, II,
     &               SUMBEFORE, SUMAFTER, DIVISOR, SOLUTION
 1000 FORMAT('{"sequence":',I12,',"runtime":"fortran",'
     & '"kind":"lu_back_substitute_row","scope":"',A,
     & '","name":null,"data":{"context":"',A,
     & '","phase":"',A,'","row":',I6,
     & ',"pivotRow":',I6,',"ii":',I6,
     & ',"sumBeforeElimination":',1PE24.16,
     & ',"sumAfterElimination":',1PE24.16,
     & ',"divisor":',1PE24.16,
     & ',"solutionValue":',1PE24.16,
     & ',"precision":"Single"},"values":null,'
     & '"tags":null,"timestampUtc":null}')
      RETURN
      END


      SUBROUTINE TRACE_LU_BACKSUB_TERM(SCOPE, CONTEXT, PHASE,
     & ROW, COL, PIVOTROW, II, MATRIXVALUE, RHSVALUE,
     & PRODUCT, SUMBEFORE, SUMAFTER)
      CHARACTER*(*) SCOPE, CONTEXT, PHASE
      INTEGER TRACE_LENTRIM, TRACE_NEXTSEQ
      INTEGER LS, LC, LP, SEQ, ROW, COL, PIVOTROW, II
      REAL MATRIXVALUE, RHSVALUE, PRODUCT, SUMBEFORE, SUMAFTER
      LOGICAL LOPEN
      CHARACTER*128 CSCOPE, CCONTEXT
      CHARACTER*32 CPHASE
C
      INQUIRE(UNIT=51, OPENED=LOPEN)
      IF(.NOT.LOPEN) RETURN
      CALL TRACE_CLEAN(SCOPE, CSCOPE)
      CALL TRACE_CLEAN(CONTEXT, CCONTEXT)
      CALL TRACE_CLEAN(PHASE, CPHASE)
      LS = TRACE_LENTRIM(CSCOPE)
      LC = TRACE_LENTRIM(CCONTEXT)
      LP = TRACE_LENTRIM(CPHASE)
      IF(LS.LE.0) LS = 1
      IF(LC.LE.0) LC = 1
      IF(LP.LE.0) LP = 1
C
      SEQ = TRACE_NEXTSEQ()
      WRITE(51,1000) SEQ, CSCOPE(1:LS), CCONTEXT(1:LC),
     &               CPHASE(1:LP), ROW, COL, PIVOTROW, II,
     &               MATRIXVALUE, RHSVALUE, PRODUCT, SUMBEFORE,
     &               SUMAFTER
 1000 FORMAT('{"sequence":',I12,',"runtime":"fortran",'
     & '"kind":"lu_back_substitute_term","scope":"',A,
     & '","name":null,"data":{"context":"',A,
     & '","phase":"',A,'","row":',I6,
     & ',"column":',I6,',"pivotRow":',I6,',"ii":',I6,
     & ',"matrixValue":',1PE24.16,',"rhsValue":',1PE24.16,
     & ',"product":',1PE24.16,',"sumBefore":',1PE24.16,
     & ',"sumAfter":',1PE24.16,
     & ',"precision":"Single"},"values":null,'
     & '"tags":null,"timestampUtc":null}')
      RETURN
      END


      SUBROUTINE TRACE_COMPRESSIBILITY_PARAMETERS(SCOPE, TKBLVAL,
     &    QINFVAL, TKBLMSVAL, HSTINVVAL, HSTINVMSVAL,
     &    RSTBLVAL, RSTBLMSVAL, REYBLVAL, REYBLREVAL, REYBLMSVAL)
      CHARACTER*(*) SCOPE
      INTEGER TRACE_LENTRIM, TRACE_NEXTSEQ
      INTEGER LS, SEQ
      REAL TKBLVAL, QINFVAL, TKBLMSVAL, HSTINVVAL, HSTINVMSVAL
      REAL RSTBLVAL, RSTBLMSVAL, REYBLVAL, REYBLREVAL, REYBLMSVAL
      LOGICAL LOPEN
      CHARACTER*128 CSCOPE
C
      INQUIRE(UNIT=51, OPENED=LOPEN)
      IF(.NOT.LOPEN) RETURN
      CALL TRACE_CLEAN(SCOPE, CSCOPE)
      LS = TRACE_LENTRIM(CSCOPE)
      IF(LS.LE.0) LS = 1
C
      SEQ = TRACE_NEXTSEQ()
      WRITE(51,1000) SEQ, CSCOPE(1:LS),
     &               TKBLVAL, QINFVAL, TKBLMSVAL, HSTINVVAL,
     &               HSTINVMSVAL, RSTBLVAL, RSTBLMSVAL,
     &               REYBLVAL, REYBLREVAL, REYBLMSVAL
 1000 FORMAT('{"sequence":',I12,',"runtime":"fortran",'
     & '"kind":"compressibility_parameters","scope":"',A,
     & '","name":null,"data":{"tkbl":',1PE24.16,
     & ',"qinfbl":',1PE24.16,',"tkbl_ms":',1PE24.16,
     & ',"hstinv":',1PE24.16,',"hstinv_ms":',1PE24.16,
     & ',"rstbl":',1PE24.16,',"rstbl_ms":',1PE24.16,
     & ',"reybl":',1PE24.16,',"reybl_re":',1PE24.16,
     & ',"reybl_ms":',1PE24.16,
     & '},"values":null,"tags":null,"timestampUtc":null}')
      RETURN
      END


      SUBROUTINE TRACE_BLKIN_INPUTS(SCOPE,
     &    U2VAL, T2VAL, D2VAL, DW2VAL,
     &    HSTINVVAL, HSTINVMSVAL,
     &    GM1BLVAL, RSTBLVAL, RSTBLMSVAL,
     &    HVRATVAL, REYBLVAL, REYBLREVAL, REYBLMSVAL)
      CHARACTER*(*) SCOPE
      INTEGER TRACE_LENTRIM, TRACE_NEXTSEQ
      INTEGER LS, SEQ
      INTEGER U2BITS, T2BITS, D2BITS
      REAL U2VAL, T2VAL, D2VAL, DW2VAL
      REAL HSTINVVAL, HSTINVMSVAL
      REAL GM1BLVAL, RSTBLVAL, RSTBLMSVAL
      REAL HVRATVAL, REYBLVAL, REYBLREVAL, REYBLMSVAL
      REAL U2TMP, T2TMP, D2TMP
      LOGICAL LOPEN
      CHARACTER*128 CSCOPE
C
      EQUIVALENCE (U2TMP, U2BITS), (T2TMP, T2BITS), (D2TMP, D2BITS)
C
      INQUIRE(UNIT=51, OPENED=LOPEN)
      IF(.NOT.LOPEN) RETURN
      CALL TRACE_CLEAN(SCOPE, CSCOPE)
      LS = TRACE_LENTRIM(CSCOPE)
      IF(LS.LE.0) LS = 1
C
      U2TMP = U2VAL
      T2TMP = T2VAL
      D2TMP = D2VAL
      SEQ = TRACE_NEXTSEQ()
      WRITE(51,1000) SEQ, CSCOPE(1:LS),
     &               U2VAL, U2BITS, T2VAL, T2BITS, D2VAL, D2BITS,
     &               DW2VAL,
     &               HSTINVVAL, HSTINVMSVAL,
     &               GM1BLVAL, RSTBLVAL, RSTBLMSVAL,
     &               HVRATVAL, REYBLVAL, REYBLREVAL, REYBLMSVAL
 1000 FORMAT('{"sequence":',I12,',"runtime":"fortran",'
     & '"kind":"blkin_inputs","scope":"',A,
     & '","name":null,"data":{"u2":',1PE24.16,
     & ',"u2Bits":',I12,',"t2":',1PE24.16,
     & ',"t2Bits":',I12,',"d2":',1PE24.16,
     & ',"d2Bits":',I12,
     & ',"dw2":',1PE24.16,',"hstinv":',1PE24.16,
     & ',"hstinv_ms":',1PE24.16,',"gm1bl":',1PE24.16,
     & ',"rstbl":',1PE24.16,',"rstbl_ms":',1PE24.16,
     & ',"hvrat":',1PE24.16,',"reybl":',1PE24.16,
     & ',"reybl_re":',1PE24.16,',"reybl_ms":',1PE24.16,
     & '},"values":null,"tags":null,"timestampUtc":null}')
      RETURN
      END


      SUBROUTINE TRACE_COMPRESSIBLE_VELOCITY(SCOPE,
     &    UEIVAL, TKBLVAL, QINFBLVAL, TKBLMSVAL,
     &    U2VAL, U2UEIVAL, U2MSVAL)
      CHARACTER*(*) SCOPE
      INTEGER TRACE_LENTRIM, TRACE_NEXTSEQ
      INTEGER LS, SEQ
      INTEGER UEIBITS, TKBLBITS, QINFBLBITS, TKBLMSBITS
      INTEGER U2BITS, U2UEIBITS, U2MSBITS
      REAL UEIVAL, TKBLVAL, QINFBLVAL, TKBLMSVAL
      REAL U2VAL, U2UEIVAL, U2MSVAL
      REAL UEITMP, TKBLTMP, QINFBLTMP, TKBLMSTMP
      REAL U2TMP, U2UEITMP, U2MSTMP
      LOGICAL LOPEN
      CHARACTER*128 CSCOPE
C
      EQUIVALENCE (UEITMP, UEIBITS), (TKBLTMP, TKBLBITS)
      EQUIVALENCE (QINFBLTMP, QINFBLBITS)
      EQUIVALENCE (TKBLMSTMP, TKBLMSBITS)
      EQUIVALENCE (U2TMP, U2BITS), (U2UEITMP, U2UEIBITS)
      EQUIVALENCE (U2MSTMP, U2MSBITS)
C
      INQUIRE(UNIT=51, OPENED=LOPEN)
      IF(.NOT.LOPEN) RETURN
      CALL TRACE_CLEAN(SCOPE, CSCOPE)
      LS = TRACE_LENTRIM(CSCOPE)
      IF(LS.LE.0) LS = 1
C
      UEITMP = UEIVAL
      TKBLTMP = TKBLVAL
      QINFBLTMP = QINFBLVAL
      TKBLMSTMP = TKBLMSVAL
      U2TMP = U2VAL
      U2UEITMP = U2UEIVAL
      U2MSTMP = U2MSVAL
      SEQ = TRACE_NEXTSEQ()
      WRITE(51,1000) SEQ, CSCOPE(1:LS),
     &               UEIVAL, UEIBITS,
     &               TKBLVAL, TKBLBITS,
     &               QINFBLVAL, QINFBLBITS,
     &               TKBLMSVAL, TKBLMSBITS,
     &               U2VAL, U2BITS,
     &               U2UEIVAL, U2UEIBITS,
     &               U2MSVAL, U2MSBITS
 1000 FORMAT('{"sequence":',I12,',"runtime":"fortran",'
     & '"kind":"compressible_velocity","scope":"',A,
     & '","name":null,"data":{"uei":',1PE24.16,
     & ',"ueiBits":',I12,',"tkbl":',1PE24.16,
     & ',"tkblBits":',I12,',"qinfbl":',1PE24.16,
     & ',"qinfblBits":',I12,',"tkbl_ms":',1PE24.16,
     & ',"tkblMsBits":',I12,',"u2":',1PE24.16,
     & ',"u2Bits":',I12,',"u2_uei":',1PE24.16,
     & ',"u2UeiBits":',I12,',"u2_ms":',1PE24.16,
     & ',"u2MsBits":',I12,
     & '},"values":null,"tags":null,"timestampUtc":null}')
      RETURN
      END


      SUBROUTINE TRACE_BLKIN_RESULT(SCOPE, U2VAL, T2VAL, D2VAL, DW2VAL,
     &    M2VAL, M2U2VAL, M2MSVAL, R2VAL, R2U2VAL, R2MSVAL, H2VAL,
     &    HK2VAL, HK2U2VAL, HK2T2VAL, HK2D2VAL, HK2MSVAL,
     &    RT2VAL, RT2U2VAL, RT2T2VAL, RT2MSVAL, RT2REVAL,
     &    HSTINVMSVAL, RSTBLMSVAL, REYBLMSVAL,
     &    HEMSVAL, V2HEVAL, V2MSREYBLTERMVAL, V2MSHETERMVAL,
     &    V2VAL, V2MSVAL)
      CHARACTER*(*) SCOPE
      INTEGER TRACE_LENTRIM, TRACE_NEXTSEQ
      INTEGER LS, SEQ
      INTEGER U2BITS, T2BITS, D2BITS, H2BITS, HK2BITS, RT2BITS
      REAL U2VAL, T2VAL, D2VAL, DW2VAL
      REAL M2VAL, M2U2VAL, M2MSVAL
      REAL R2VAL, R2U2VAL, R2MSVAL, H2VAL
      REAL HK2VAL, HK2U2VAL, HK2T2VAL, HK2D2VAL, HK2MSVAL
      REAL RT2VAL, RT2U2VAL, RT2T2VAL, RT2MSVAL, RT2REVAL
      REAL HSTINVMSVAL, RSTBLMSVAL, REYBLMSVAL
      REAL HEMSVAL, V2HEVAL, V2MSREYBLTERMVAL, V2MSHETERMVAL
      REAL V2VAL, V2MSVAL
      REAL U2TMP, T2TMP, D2TMP, H2TMP, HK2TMP, RT2TMP
      LOGICAL LOPEN
      CHARACTER*128 CSCOPE
C
      EQUIVALENCE (U2TMP, U2BITS), (T2TMP, T2BITS), (D2TMP, D2BITS)
      EQUIVALENCE (H2TMP, H2BITS), (HK2TMP, HK2BITS)
      EQUIVALENCE (RT2TMP, RT2BITS)
C
      INQUIRE(UNIT=51, OPENED=LOPEN)
      IF(.NOT.LOPEN) RETURN
      CALL TRACE_CLEAN(SCOPE, CSCOPE)
      LS = TRACE_LENTRIM(CSCOPE)
      IF(LS.LE.0) LS = 1
C
      U2TMP = U2VAL
      T2TMP = T2VAL
      D2TMP = D2VAL
      H2TMP = H2VAL
      HK2TMP = HK2VAL
      RT2TMP = RT2VAL
      SEQ = TRACE_NEXTSEQ()
      WRITE(51,1000) SEQ, CSCOPE(1:LS),
     &               U2VAL, U2BITS, T2VAL, T2BITS, D2VAL, D2BITS,
     &               DW2VAL,
     &               M2VAL, M2U2VAL, M2MSVAL,
     &               R2VAL, R2U2VAL, R2MSVAL, H2VAL, H2BITS,
     &               HK2VAL, HK2BITS,
     &               HK2U2VAL, HK2T2VAL, HK2D2VAL, HK2MSVAL,
     &               RT2VAL, RT2BITS,
     &               RT2U2VAL, RT2T2VAL, RT2MSVAL, RT2REVAL,
     &               HSTINVMSVAL, RSTBLMSVAL, REYBLMSVAL,
     &               HEMSVAL, V2HEVAL, V2MSREYBLTERMVAL,
     &               V2MSHETERMVAL, V2VAL, V2MSVAL
 1000 FORMAT('{"sequence":',I12,',"runtime":"fortran",'
     & '"kind":"kinematic_result","scope":"',A,
     & '","name":null,"data":{"u2":',1PE24.16,
     & ',"u2Bits":',I10,',"t2":',1PE24.16,
     & ',"t2Bits":',I10,',"d2":',1PE24.16,
     & ',"d2Bits":',I10,
     & ',"dw2":',1PE24.16,',"m2":',1PE24.16,
     & ',"m2_u2":',1PE24.16,',"m2_ms":',1PE24.16,
     & ',"r2":',1PE24.16,',"r2_u2":',1PE24.16,
     & ',"r2_ms":',1PE24.16,',"h2":',1PE24.16,
     & ',"h2Bits":',I10,',"hK2":',1PE24.16,
     & ',"hK2Bits":',I10,',"hK2_u2":',1PE24.16,
     & ',"hK2_t2":',1PE24.16,',"hK2_d2":',1PE24.16,
     & ',"hK2_ms":',1PE24.16,',"rT2":',1PE24.16,
     & ',"rT2Bits":',I10,',"rT2_u2":',1PE24.16,
     & ',"rT2_t2":',1PE24.16,
     & ',"rT2_ms":',1PE24.16,',"rT2_re":',1PE24.16,
     & ',"hstinv_ms":',1PE24.16,',"rstbl_ms":',1PE24.16,
     & ',"reybl_ms":',1PE24.16,
     & ',"he_ms":',1PE24.16,',"v2_he":',1PE24.16,
     & ',"v2MsReyblTerm":',1PE24.16,
     & ',"v2MsHeTerm":',1PE24.16,
     & ',"v2":',1PE24.16,
     & ',"v2_ms":',1PE24.16,
     & '},"values":null,"tags":null,"timestampUtc":null}')
      RETURN
      END


      SUBROUTINE TRACE_BLKIN_TERMS(SCOPE, U2SQHSTINVVAL, M2DENVAL,
     &    TR2VAL, M2MSNUMVAL, M2MSVAL)
      CHARACTER*(*) SCOPE
      INTEGER TRACE_LENTRIM, TRACE_NEXTSEQ
      INTEGER LS, SEQ
      REAL U2SQHSTINVVAL, M2DENVAL, TR2VAL, M2MSNUMVAL, M2MSVAL
      LOGICAL LOPEN
      CHARACTER*128 CSCOPE
C
      INQUIRE(UNIT=51, OPENED=LOPEN)
      IF(.NOT.LOPEN) RETURN
      CALL TRACE_CLEAN(SCOPE, CSCOPE)
      LS = TRACE_LENTRIM(CSCOPE)
      IF(LS.LE.0) LS = 1
C
      SEQ = TRACE_NEXTSEQ()
      WRITE(51,1000) SEQ, CSCOPE(1:LS),
     &               U2SQHSTINVVAL, M2DENVAL, TR2VAL,
     &               M2MSNUMVAL, M2MSVAL
 1000 FORMAT('{"sequence":',I12,',"runtime":"fortran",'
     & '"kind":"blkin_terms","scope":"',A,
     & '","name":null,"data":{"u2sqHstinv":',1PE24.16,
     & ',"m2Den":',1PE24.16,',"tr2":',1PE24.16,
     & ',"m2MsNum":',1PE24.16,',"m2Ms":',1PE24.16,
     & '},"values":null,"tags":null,"timestampUtc":null}')
      RETURN
      END


      SUBROUTINE TRACE_BLDIF_PRIMARY_STATION(SCOPE, ITYP, STATION,
     &    XVAL, UVAL, TVAL, DVAL, SVAL, MSQVAL, HVAL, HKVAL, RTVAL)
      CHARACTER*(*) SCOPE
      INTEGER TRACE_LENTRIM, TRACE_NEXTSEQ
      INTEGER LS, SEQ, ITYP, STATION
      REAL XVAL, UVAL, TVAL, DVAL, SVAL, MSQVAL, HVAL, HKVAL, RTVAL
      LOGICAL LOPEN
      CHARACTER*128 CSCOPE
C
      INQUIRE(UNIT=51, OPENED=LOPEN)
      IF(.NOT.LOPEN) RETURN
      CALL TRACE_CLEAN(SCOPE, CSCOPE)
      LS = TRACE_LENTRIM(CSCOPE)
      IF(LS.LE.0) LS = 1
C
      SEQ = TRACE_NEXTSEQ()
      WRITE(51,1000) SEQ, CSCOPE(1:LS), ITYP, STATION,
     &               XVAL, UVAL, TVAL, DVAL, SVAL,
     &               MSQVAL, HVAL, HKVAL, RTVAL
 1000 FORMAT('{"sequence":',I12,',"runtime":"fortran",'
     & '"kind":"bldif_primary_station","scope":"',A,
     & '","name":null,"data":{"ityp":',I2,
     & ',"station":',I1,',"x":',1PE24.16,
     & ',"u":',1PE24.16,',"t":',1PE24.16,
     & ',"d":',1PE24.16,',"s":',1PE24.16,
     & ',"msq":',1PE24.16,',"h":',1PE24.16,
     & ',"hk":',1PE24.16,',"rt":',1PE24.16,
     & '},"values":null,"tags":null,"timestampUtc":null}')
      RETURN
      END


      SUBROUTINE TRACE_BLVAR_CF_TERMS(SCOPE, ITYP, STATION,
     &    HKMSVAL, RTMSVAL, MMSVAL,
     &    CFVAL, CFHKVAL, CFRTVAL, CFMVAL,
     &    CFUVAL, CFTVAL, CFDVAL, CFMSVAL, RTREVAL, CFREVAL)
      CHARACTER*(*) SCOPE
      INTEGER TRACE_LENTRIM, TRACE_NEXTSEQ
      INTEGER LS, SEQ, ITYP, STATION
      REAL HKMSVAL, RTMSVAL, MMSVAL
      REAL CFVAL, CFHKVAL, CFRTVAL, CFMVAL
      REAL CFUVAL, CFTVAL, CFDVAL, CFMSVAL, RTREVAL, CFREVAL
      LOGICAL LOPEN
      CHARACTER*128 CSCOPE
C
      INQUIRE(UNIT=51, OPENED=LOPEN)
      IF(.NOT.LOPEN) RETURN
      CALL TRACE_CLEAN(SCOPE, CSCOPE)
      LS = TRACE_LENTRIM(CSCOPE)
      IF(LS.LE.0) LS = 1
C
      SEQ = TRACE_NEXTSEQ()
      WRITE(51,1000) SEQ, CSCOPE(1:LS), ITYP, STATION,
     &               HKMSVAL, RTMSVAL, MMSVAL,
     &               CFVAL, CFHKVAL, CFRTVAL, CFMVAL,
     &               CFUVAL, CFTVAL, CFDVAL, CFMSVAL, RTREVAL,
     &               CFREVAL
 1000 FORMAT('{"sequence":',I12,',"runtime":"fortran",'
     & '"kind":"blvar_cf_terms","scope":"',A,
     & '","name":null,"data":{"ityp":',I2,
     & ',"station":',I1,',"hkMs":',1PE24.16,
     & ',"rtMs":',1PE24.16,',"mMs":',1PE24.16,
     & ',"cf":',1PE24.16,
     & ',"cfHk":',1PE24.16,',"cfRt":',1PE24.16,
     & ',"cfM":',1PE24.16,',"cfU":',1PE24.16,
     & ',"cfT":',1PE24.16,',"cfD":',1PE24.16,
     & ',"cfMs":',1PE24.16,',"rtRe":',1PE24.16,
     & ',"cfRe":',1PE24.16,
     & '},"values":null,"tags":null,"timestampUtc":null}')
      RETURN
      END


      SUBROUTINE TRACE_BLMID_CANDIDATE_CF_TERMS(SCOPE, ITYP,
     &    HKAVAL, RTAVAL, MAVAL,
     &    CFMTURBVAL, CFMTURBHKAVAL, CFMTURBRTAVAL, CFMTURBMAVAL,
     &    CFMLAMVAL, CFMLAMHKAVAL, CFMLAMRTAVAL, CFMLAMMAVAL,
     &    USEDLAMVAL, CFMVAL, CFMHKAVAL, CFMRTAVAL, CFMMAVAL)
      CHARACTER*(*) SCOPE
      INTEGER TRACE_LENTRIM, TRACE_NEXTSEQ
      INTEGER LS, SEQ, ITYP, USEDLAMVAL
      REAL HKAVAL, RTAVAL, MAVAL
      REAL CFMTURBVAL, CFMTURBHKAVAL, CFMTURBRTAVAL, CFMTURBMAVAL
      REAL CFMLAMVAL, CFMLAMHKAVAL, CFMLAMRTAVAL, CFMLAMMAVAL
      REAL CFMVAL, CFMHKAVAL, CFMRTAVAL, CFMMAVAL
      LOGICAL LOPEN
      CHARACTER*128 CSCOPE
C
      INQUIRE(UNIT=51, OPENED=LOPEN)
      IF(.NOT.LOPEN) RETURN
      CALL TRACE_CLEAN(SCOPE, CSCOPE)
      LS = TRACE_LENTRIM(CSCOPE)
      IF(LS.LE.0) LS = 1
C
      SEQ = TRACE_NEXTSEQ()
      WRITE(51,1000) SEQ, CSCOPE(1:LS), ITYP,
     &               HKAVAL, RTAVAL, MAVAL,
     &               CFMTURBVAL, CFMTURBHKAVAL,
     &               CFMTURBRTAVAL, CFMTURBMAVAL,
     &               CFMLAMVAL, CFMLAMHKAVAL,
     &               CFMLAMRTAVAL, CFMLAMMAVAL,
     &               USEDLAMVAL,
     &               CFMVAL, CFMHKAVAL, CFMRTAVAL, CFMMAVAL
 1000 FORMAT('{"sequence":',I12,',"runtime":"fortran",'
     & '"kind":"blmid_candidate_cf_terms","scope":"',A,
     & '","name":null,"data":{"ityp":',I2,
     & ',"hka":',1PE24.16,',"rta":',1PE24.16,
     & ',"ma":',1PE24.16,',"cfmTurb":',1PE24.16,
     & ',"cfmTurbHka":',1PE24.16,',"cfmTurbRta":',1PE24.16,
     & ',"cfmTurbMa":',1PE24.16,',"cfmLam":',1PE24.16,
     & ',"cfmLamHka":',1PE24.16,',"cfmLamRta":',1PE24.16,
     & ',"cfmLamMa":',1PE24.16,',"usedLaminar":',I2,
     & ',"cfm":',1PE24.16,',"cfmHka":',1PE24.16,
     & ',"cfmRta":',1PE24.16,',"cfmMa":',1PE24.16,
     & '},"values":null,"tags":null,"timestampUtc":null}')
      RETURN
      END


      SUBROUTINE TRACE_BLMID_CF_TERMS(SCOPE, ITYP,
     &    HK1MSVAL, RT1MSVAL, M1MSVAL,
     &    HK2MSVAL, RT2MSVAL, M2MSVAL,
     &    CFMVAL, CFMHKAVAL, CFMRTAVAL, CFMMAVAL,
     &    HK1T1VAL, RT1T1VAL, HK2T2VAL, RT2T2VAL,
     &    CFMU1VAL, CFMT1VAL, CFMD1VAL,
     &    CFMU2VAL, CFMT2VAL, CFMD2VAL,
     &    CFMMSVAL, RT1REVAL, RT2REVAL, CFMREVAL)
      CHARACTER*(*) SCOPE
      INTEGER TRACE_LENTRIM, TRACE_NEXTSEQ
      INTEGER LS, SEQ, ITYP
      REAL HK1MSVAL, RT1MSVAL, M1MSVAL
      REAL HK2MSVAL, RT2MSVAL, M2MSVAL
      REAL CFMVAL, CFMHKAVAL, CFMRTAVAL, CFMMAVAL
      REAL HK1T1VAL, RT1T1VAL, HK2T2VAL, RT2T2VAL
      REAL CFMU1VAL, CFMT1VAL, CFMD1VAL
      REAL CFMU2VAL, CFMT2VAL, CFMD2VAL
      REAL CFMMSVAL, RT1REVAL, RT2REVAL, CFMREVAL
      LOGICAL LOPEN
      CHARACTER*128 CSCOPE
C
      INQUIRE(UNIT=51, OPENED=LOPEN)
      IF(.NOT.LOPEN) RETURN
      CALL TRACE_CLEAN(SCOPE, CSCOPE)
      LS = TRACE_LENTRIM(CSCOPE)
      IF(LS.LE.0) LS = 1
C
      SEQ = TRACE_NEXTSEQ()
      WRITE(51,1000) SEQ, CSCOPE(1:LS), ITYP,
     &               HK1MSVAL, RT1MSVAL, M1MSVAL,
     &               HK2MSVAL, RT2MSVAL, M2MSVAL,
     &               CFMVAL, CFMHKAVAL, CFMRTAVAL, CFMMAVAL,
     &               HK1T1VAL, RT1T1VAL, HK2T2VAL, RT2T2VAL,
     &               CFMU1VAL, CFMT1VAL, CFMD1VAL,
     &               CFMU2VAL, CFMT2VAL, CFMD2VAL,
     &               CFMMSVAL, RT1REVAL, RT2REVAL, CFMREVAL
 1000 FORMAT('{"sequence":',I12,',"runtime":"fortran",'
     & '"kind":"blmid_cf_terms","scope":"',A,
     & '","name":null,"data":{"ityp":',I2,
     & ',"hk1Ms":',1PE24.16,',"rt1Ms":',1PE24.16,
     & ',"m1Ms":',1PE24.16,',"hk2Ms":',1PE24.16,
     & ',"rt2Ms":',1PE24.16,',"m2Ms":',1PE24.16,
     & ',"cfm":',1PE24.16,',"cfmHka":',1PE24.16,
     & ',"cfmRta":',1PE24.16,',"cfmMa":',1PE24.16,
     & ',"hk1T1":',1PE24.16,',"rt1T1":',1PE24.16,
     & ',"hk2T2":',1PE24.16,',"rt2T2":',1PE24.16,
     & ',"cfmU1":',1PE24.16,',"cfmT1":',1PE24.16,
     & ',"cfmD1":',1PE24.16,',"cfmU2":',1PE24.16,
     & ',"cfmT2":',1PE24.16,',"cfmD2":',1PE24.16,
     & ',"cfmMs":',1PE24.16,',"rt1Re":',1PE24.16,
     & ',"rt2Re":',1PE24.16,',"cfmRe":',1PE24.16,
     & '},"values":null,"tags":null,"timestampUtc":null}')
      RETURN
      END


      SUBROUTINE TRACE_TRANSITION_SENSITIVITIES(SCOPE,
     &    AXVAL, AXHK1VAL, AXT1VAL, AXRT1VAL, AXA1VAL,
     &    AXHK2VAL, AXT2VAL, AXRT2VAL, AXA2VAL)
      CHARACTER*(*) SCOPE
      INTEGER TRACE_LENTRIM, TRACE_NEXTSEQ
      INTEGER LS, SEQ
      REAL AXVAL, AXHK1VAL, AXT1VAL, AXRT1VAL, AXA1VAL
      REAL AXHK2VAL, AXT2VAL, AXRT2VAL, AXA2VAL
      LOGICAL LOPEN
      CHARACTER*128 CSCOPE
C
      INQUIRE(UNIT=51, OPENED=LOPEN)
      IF(.NOT.LOPEN) RETURN
      CALL TRACE_CLEAN(SCOPE, CSCOPE)
      LS = TRACE_LENTRIM(CSCOPE)
      IF(LS.LE.0) LS = 1
C
      SEQ = TRACE_NEXTSEQ()
      WRITE(51,1000) SEQ, CSCOPE(1:LS),
     &               AXVAL, AXHK1VAL, AXT1VAL, AXRT1VAL, AXA1VAL,
     &               AXHK2VAL, AXT2VAL, AXRT2VAL, AXA2VAL
 1000 FORMAT('{"sequence":',I12,',"runtime":"fortran",'
     & '"kind":"transition_sensitivities","scope":"',A,
     & '","name":null,"data":{"ax":',1PE24.16,
     & ',"ax_Hk1":',1PE24.16,',"ax_T1":',1PE24.16,
     & ',"ax_Rt1":',1PE24.16,',"ax_A1":',1PE24.16,
     & ',"ax_Hk2":',1PE24.16,',"ax_T2":',1PE24.16,
     & ',"ax_Rt2":',1PE24.16,',"ax_A2":',1PE24.16,
     & '},"values":null,"tags":null,"timestampUtc":null}')
      RETURN
      END


      SUBROUTINE TRACE_TRANSITION_SENSITIVITY_INPUTS(SCOPE,
     &    HK1VAL, T1VAL, RT1VAL, A1VAL,
     &    HK2VAL, T2VAL, RT2VAL, A2VAL, ACRITVAL, IDAMPVAL)
      CHARACTER*(*) SCOPE
      INTEGER TRACE_LENTRIM, TRACE_NEXTSEQ
      INTEGER LS, SEQ, IDAMPVAL
      REAL HK1VAL, T1VAL, RT1VAL, A1VAL
      REAL HK2VAL, T2VAL, RT2VAL, A2VAL, ACRITVAL
      LOGICAL LOPEN
      CHARACTER*128 CSCOPE
C
      INQUIRE(UNIT=51, OPENED=LOPEN)
      IF(.NOT.LOPEN) RETURN
      CALL TRACE_CLEAN(SCOPE, CSCOPE)
      LS = TRACE_LENTRIM(CSCOPE)
      IF(LS.LE.0) LS = 1
C
      SEQ = TRACE_NEXTSEQ()
      WRITE(51,1000) SEQ, CSCOPE(1:LS),
     &               HK1VAL, T1VAL, RT1VAL, A1VAL,
     &               HK2VAL, T2VAL, RT2VAL, A2VAL, ACRITVAL, IDAMPVAL
 1000 FORMAT('{"sequence":',I12,',"runtime":"fortran",'
     & '"kind":"transition_sensitivity_inputs","scope":"',A,
     & '","name":null,"data":{"hk1":',1PE24.16,
     & ',"t1":',1PE24.16,',"rt1":',1PE24.16,
     & ',"a1":',1PE24.16,',"hk2":',1PE24.16,
     & ',"t2":',1PE24.16,',"rt2":',1PE24.16,
     & ',"a2":',1PE24.16,',"acrit":',1PE24.16,
     & ',"idampv":',I2,
     & '},"values":null,"tags":null,"timestampUtc":null}')
      RETURN
      END


      SUBROUTINE TRACE_BLDIF_SECONDARY_STATION(SCOPE, ITYP, STATION,
     &    HCVAL, HSVAL, HSHKVAL, HKDVAL, HSDVAL, HSTVAL,
     &    USVAL, USTVAL, HKUVAL, RTTVAL, RTUVAL,
     &    CQVAL, CFVAL, CFUVAL, CFTVAL, CFDVAL, CFMSVAL,
     &    CFMUVAL, CFMTVAL, CFMDVAL, CFMMSVAL,
     &    DIVAL, DITVAL, DEVAL)
      CHARACTER*(*) SCOPE
      INTEGER TRACE_LENTRIM, TRACE_NEXTSEQ
      INTEGER LS, SEQ, ITYP, STATION
      REAL HCVAL, HSVAL, HSHKVAL, HKDVAL, HSDVAL, HSTVAL
      REAL USVAL, USTVAL, HKUVAL, RTTVAL, RTUVAL
      REAL CQVAL, CFVAL, CFUVAL, CFTVAL, CFDVAL, CFMSVAL
      REAL CFMUVAL, CFMTVAL, CFMDVAL, CFMMSVAL, DIVAL, DITVAL, DEVAL
      LOGICAL LOPEN
      CHARACTER*128 CSCOPE
C
      INQUIRE(UNIT=51, OPENED=LOPEN)
      IF(.NOT.LOPEN) RETURN
      CALL TRACE_CLEAN(SCOPE, CSCOPE)
      LS = TRACE_LENTRIM(CSCOPE)
      IF(LS.LE.0) LS = 1
C
      SEQ = TRACE_NEXTSEQ()
      WRITE(51,1000) SEQ, CSCOPE(1:LS), ITYP, STATION,
     &               HCVAL, HSVAL, HSHKVAL, HKDVAL, HSDVAL, HSTVAL,
     &               USVAL, USTVAL, HKUVAL, RTTVAL, RTUVAL,
     &               CQVAL, CFVAL, CFUVAL, CFTVAL, CFDVAL, CFMSVAL,
     &               CFMUVAL, CFMTVAL, CFMDVAL, CFMMSVAL,
     &               DIVAL, DITVAL, DEVAL
 1000 FORMAT('{"sequence":',I12,',"runtime":"fortran",'
     & '"kind":"bldif_secondary_station","scope":"',A,
     & '","name":null,"data":{"ityp":',I2,
     & ',"station":',I1,',"hc":',1PE24.16,
     & ',"hs":',1PE24.16,',"hsHk":',1PE24.16,
     & ',"hkD":',1PE24.16,',"hsD":',1PE24.16,
     & ',"hsT":',1PE24.16,',"us":',1PE24.16,
     & ',"usT":',1PE24.16,',"hkU":',1PE24.16,
     & ',"rtT":',1PE24.16,',"rtU":',1PE24.16,
     & ',"cq":',1PE24.16,',"cf":',1PE24.16,
     & ',"cfU":',1PE24.16,',"cfT":',1PE24.16,
     & ',"cfD":',1PE24.16,',"cfMs":',1PE24.16,
     & ',"cfmU":',1PE24.16,',"cfmT":',1PE24.16,
     & ',"cfmD":',1PE24.16,',"cfmMs":',1PE24.16,
     & ',"di":',1PE24.16,',"diT":',1PE24.16,
     & ',"de":',1PE24.16,
     & '},"values":null,"tags":null,"timestampUtc":null}')
      RETURN
      END


      SUBROUTINE TRACE_HSL_TERMS(SCOPE, HKVAL, HSVAL, HSHKVAL,
     &                           TMPVAL, HKP1VAL, HSHK1VAL, HSHK2VAL,
     &                           HSHK3VAL)
      CHARACTER*(*) SCOPE
      INTEGER TRACE_LENTRIM, TRACE_NEXTSEQ
      INTEGER LS, SEQ
      REAL HKVAL, HSVAL, HSHKVAL, TMPVAL, HKP1VAL
      REAL HSHK1VAL, HSHK2VAL, HSHK3VAL
      LOGICAL LOPEN
      CHARACTER*128 CSCOPE
C
      INQUIRE(UNIT=51, OPENED=LOPEN)
      IF(.NOT.LOPEN) RETURN
      CALL TRACE_CLEAN(SCOPE, CSCOPE)
      LS = TRACE_LENTRIM(CSCOPE)
      IF(LS.LE.0) LS = 1
      SEQ = TRACE_NEXTSEQ()
      WRITE(51,1000) SEQ, CSCOPE(1:LS), HKVAL, HSVAL, HSHKVAL,
     &               TMPVAL, HKP1VAL, HSHK1VAL, HSHK2VAL, HSHK3VAL
 1000 FORMAT('{"sequence":',I12,',"runtime":"fortran",'
     & '"kind":"hsl_terms","scope":"',A,
     & '","name":null,"data":{"hk":',1PE24.16,
     & ',"hs":',1PE24.16,',"hsHk":',1PE24.16,
     & ',"tmp":',1PE24.16,',"hkPlusOne":',1PE24.16,
     & ',"hsHkTerm1":',1PE24.16,',"hsHkTerm2":',1PE24.16,
     & ',"hsHkTerm3":',1PE24.16,
     & '},"values":null,"tags":null,"timestampUtc":null}')
      RETURN
      END


      SUBROUTINE TRACE_CFT_TERMS(SCOPE, HKVAL, RTVAL, MSQVAL,
     &     FCVAL, GRTVAL, GEXVAL, ARGVAL, THKVAL,
     &     THKSQVAL, ONEMTHKSQVAL, SCALEDTHKDIFFVAL, CFOVAL,
     &     CFHKTERM1VAL, CFHKTERM2VAL, CFHKTERM3VAL,
     &     CFVAL, CFHKVAL, CFRTVAL, CFMSQVAL)
      CHARACTER*(*) SCOPE
      INTEGER TRACE_LENTRIM, TRACE_NEXTSEQ
      INTEGER LS, SEQ
      REAL HKVAL, RTVAL, MSQVAL, FCVAL, GRTVAL, GEXVAL
      REAL ARGVAL, THKVAL, THKSQVAL, ONEMTHKSQVAL, SCALEDTHKDIFFVAL
      REAL CFOVAL
      REAL CFHKTERM1VAL, CFHKTERM2VAL, CFHKTERM3VAL
      REAL CFVAL, CFHKVAL, CFRTVAL, CFMSQVAL
      LOGICAL LOPEN
      CHARACTER*128 CSCOPE
C
      INQUIRE(UNIT=51, OPENED=LOPEN)
      IF(.NOT.LOPEN) RETURN
      CALL TRACE_CLEAN(SCOPE, CSCOPE)
      LS = TRACE_LENTRIM(CSCOPE)
      IF(LS.LE.0) LS = 1
      SEQ = TRACE_NEXTSEQ()
      WRITE(51,1000) SEQ, CSCOPE(1:LS),
     & HKVAL, RTVAL, MSQVAL, FCVAL, GRTVAL, GEXVAL,
     & ARGVAL, THKVAL, THKSQVAL, ONEMTHKSQVAL, SCALEDTHKDIFFVAL,
     & CFOVAL,
     & CFHKTERM1VAL, CFHKTERM2VAL, CFHKTERM3VAL,
     & CFVAL, CFHKVAL, CFRTVAL, CFMSQVAL
 1000 FORMAT('{"sequence":',I12,',"runtime":"fortran",'
     & '"kind":"cft_terms","scope":"',A,
     & '","name":null,"data":{"hk":',1PE24.16,
     & ',"rt":',1PE24.16,',"msq":',1PE24.16,
     & ',"fc":',1PE24.16,',"grt":',1PE24.16,
     & ',"gex":',1PE24.16,',"arg":',1PE24.16,
     & ',"thk":',1PE24.16,',"thkSq":',1PE24.16,
     & ',"oneMinusThkSq":',1PE24.16,
     & ',"scaledThkDiff":',1PE24.16,
     & ',"cfo":',1PE24.16,
     & ',"cfHkTerm1":',1PE24.16,',"cfHkTerm2":',1PE24.16,
     & ',"cfHkTerm3":',1PE24.16,',"cf":',1PE24.16,
     & ',"cfHk":',1PE24.16,',"cfRt":',1PE24.16,
     & ',"cfMsq":',1PE24.16,
     & '},"values":null,"tags":null,"timestampUtc":null}')
      RETURN
      END


      SUBROUTINE TRACE_HST_TERMS(SCOPE, HKVAL, RTVAL, MSQVAL,
     &                           BRANCHVAL, HOVAL, HORTVAL,
     &                           RTZVAL, RTZRTVAL,
     &                           GRTVAL, HDIFVAL, RTMPVAL,
     &                           HTMPVAL, HTMPHKVAL, HTMPRTVAL,
     &                           HSHKTERM1VAL, HSHKTERM2VAL,
     &                           HSRTRAWVAL,
     &                           HSRTTERM1VAL, HSRTTERM2VAL,
     &                           HSRTTERM3VAL,
     &                           HSVAL, HSHKVAL, HSRTVAL,
     &                           HSMSQVAL, FMVAL)
      CHARACTER*(*) SCOPE
      INTEGER TRACE_LENTRIM, TRACE_NEXTSEQ
      INTEGER LS, SEQ
      REAL HKVAL, RTVAL, MSQVAL, BRANCHVAL, HOVAL, HORTVAL
      REAL RTZVAL, RTZRTVAL, GRTVAL, HDIFVAL, RTMPVAL
      REAL HTMPVAL, HTMPHKVAL, HTMPRTVAL
      REAL HSHKTERM1VAL, HSHKTERM2VAL, HSRTRAWVAL
      REAL HSRTTERM1VAL, HSRTTERM2VAL, HSRTTERM3VAL
      REAL HSVAL, HSHKVAL, HSRTVAL, HSMSQVAL, FMVAL
      LOGICAL LOPEN
      CHARACTER*128 CSCOPE
C
      INQUIRE(UNIT=51, OPENED=LOPEN)
      IF(.NOT.LOPEN) RETURN
      CALL TRACE_CLEAN(SCOPE, CSCOPE)
      LS = TRACE_LENTRIM(CSCOPE)
      IF(LS.LE.0) LS = 1
      SEQ = TRACE_NEXTSEQ()
      WRITE(51,1000) SEQ, CSCOPE(1:LS),
     & HKVAL, RTVAL, MSQVAL, BRANCHVAL, HOVAL, HORTVAL,
     & RTZVAL, RTZRTVAL, GRTVAL, HDIFVAL, RTMPVAL,
     & HTMPVAL, HTMPHKVAL, HTMPRTVAL,
     & HSHKTERM1VAL, HSHKTERM2VAL, HSRTRAWVAL,
     & HSRTTERM1VAL, HSRTTERM2VAL, HSRTTERM3VAL,
     & HSVAL, HSHKVAL, HSRTVAL, HSMSQVAL, FMVAL
 1000 FORMAT('{"sequence":',I12,',"runtime":"fortran",'
     & '"kind":"hst_terms","scope":"',A,
     & '","name":null,"data":{"hk":',1PE24.16,
     & ',"rt":',1PE24.16,',"msq":',1PE24.16,
     & ',"branch":',1PE24.16,',"ho":',1PE24.16,
     & ',"hoRt":',1PE24.16,',"rtz":',1PE24.16,
     & ',"rtzRt":',1PE24.16,',"grt":',1PE24.16,
     & ',"hdif":',1PE24.16,',"rtmp":',1PE24.16,
     & ',"htmp":',1PE24.16,',"htmpHk":',1PE24.16,
     & ',"htmpRt":',1PE24.16,
     & ',"hsHkTerm1":',1PE24.16,',"hsHkTerm2":',1PE24.16,
     & ',"hsRtRaw":',1PE24.16,',"hsRtTerm1":',1PE24.16,
     & ',"hsRtTerm2":',1PE24.16,',"hsRtTerm3":',1PE24.16,
     & ',"hs":',1PE24.16,
     & ',"hsHk":',1PE24.16,',"hsRt":',1PE24.16,
     & ',"hsMsq":',1PE24.16,',"fm":',1PE24.16,
     & '},"values":null,"tags":null,"timestampUtc":null}')
      RETURN
      END


      SUBROUTINE TRACE_SEED_INVERSE_TARGET(SCOPE, ISIDE, ISTATION,
     &     IITER, HK1VAL, X1VAL, X2VAL, T1VAL, XTVAL,
     &     HKTESTVAL, HMAXVAL,
     &     HTRAWVAL, HTARGVAL)
      CHARACTER*(*) SCOPE
      INTEGER TRACE_LENTRIM, TRACE_NEXTSEQ
      INTEGER LS, SEQ, ISIDE, ISTATION, IITER
      REAL HK1VAL, X1VAL, X2VAL, T1VAL, XTVAL
      REAL HKTESTVAL, HMAXVAL, HTRAWVAL, HTARGVAL
      LOGICAL LOPEN
      CHARACTER*128 CSCOPE
C
      INQUIRE(UNIT=51, OPENED=LOPEN)
      IF(.NOT.LOPEN) RETURN
      CALL TRACE_CLEAN(SCOPE, CSCOPE)
      LS = TRACE_LENTRIM(CSCOPE)
      IF(LS.LE.0) LS = 1
C
      SEQ = TRACE_NEXTSEQ()
      WRITE(51,1000) SEQ, CSCOPE(1:LS),
     &     ISIDE, ISTATION, IITER,
     &     HK1VAL, X1VAL, X2VAL, T1VAL, XTVAL,
     &     HKTESTVAL, HMAXVAL, HTRAWVAL, HTARGVAL
 1000 FORMAT('{"sequence":',I12,',"runtime":"fortran",'
     & '"kind":"laminar_seed_inverse_target","scope":"',A,
     & '","name":null,"data":{"side":',I6,
     & ',"station":',I6,',"iteration":',I6,
     & ',"hk1":',1PE24.16,',"x1":',1PE24.16,
     & ',"x2":',1PE24.16,',"theta1":',1PE24.16,
     & ',"transitionXi":',1PE24.16,',"hkTest":',1PE24.16,
     & ',"hmax":',1PE24.16,',"htargRaw":',1PE24.16,
     & ',"htarg":',1PE24.16,
     & '},"values":null,"tags":null,"timestampUtc":null}')
      RETURN
      END


      SUBROUTINE TRACE_BLVAR_LAMINAR_DI_TERMS(SCOPE, STATION,
     &    HKVAL, RTVAL, DIVAL, DIHKVAL, DIRTVAL, HKTVAL, RTTVAL, DITVAL)
      CHARACTER*(*) SCOPE
      INTEGER TRACE_LENTRIM, TRACE_NEXTSEQ
      INTEGER LS, SEQ, STATION
      REAL HKVAL, RTVAL, DIVAL, DIHKVAL, DIRTVAL, HKTVAL, RTTVAL, DITVAL
      LOGICAL LOPEN
      CHARACTER*128 CSCOPE
C
      INQUIRE(UNIT=51, OPENED=LOPEN)
      IF(.NOT.LOPEN) RETURN
      CALL TRACE_CLEAN(SCOPE, CSCOPE)
      LS = TRACE_LENTRIM(CSCOPE)
      IF(LS.LE.0) LS = 1
C
      SEQ = TRACE_NEXTSEQ()
      WRITE(51,1000) SEQ, CSCOPE(1:LS), STATION,
     &               HKVAL, RTVAL, DIVAL, DIHKVAL, DIRTVAL, HKTVAL,
     &               RTTVAL, DITVAL
 1000 FORMAT('{"sequence":',I12,',"runtime":"fortran",'
     & '"kind":"blvar_laminar_di_terms","scope":"',A,
     & '","name":null,"data":{"station":',I1,
     & ',"hk":',1PE24.16,',"rt":',1PE24.16,
     & ',"di":',1PE24.16,',"diHk":',1PE24.16,
     & ',"diRt":',1PE24.16,',"hkT":',1PE24.16,
     & ',"rtT":',1PE24.16,',"diT":',1PE24.16,
     & '},"values":null,"tags":null,"timestampUtc":null}')
      RETURN
      END


      SUBROUTINE TRACE_BLVAR_TURBULENT_D_UPDATE(SCOPE,
     &    SVAL, DIWALLDPOST, DDHSVAL, HSHKVAL, HKDVAL, HSDVAL,
     &    DDUSVAL, USDVAL,
     &    DDDVAL, DDLHSVAL, DDLUSVAL, DDLDVAL, FINALDIDVAL)
      CHARACTER*(*) SCOPE
      INTEGER TRACE_LENTRIM, TRACE_NEXTSEQ
      INTEGER LS, SEQ
      REAL SVAL, DIWALLDPOST, DDHSVAL, HSHKVAL, HKDVAL, HSDVAL
      REAL DDUSVAL, USDVAL
      REAL DDDVAL, DDLHSVAL, DDLUSVAL, DDLDVAL, FINALDIDVAL
      LOGICAL LOPEN
      CHARACTER*128 CSCOPE
      CHARACTER*8 SVALHEX, DIWALLDPOSTHEX, DDHSHEX, HSHKHEX
      CHARACTER*8 HKDHEX, HSDHEX
      CHARACTER*8 DDUSHEX, USDHEX, DDDHEX, DDLHSHEX
      CHARACTER*8 DDLUSHEX, DDLDHEX, FINALDIDHEX
C
      INQUIRE(UNIT=51, OPENED=LOPEN)
      IF(.NOT.LOPEN) RETURN
      CALL TRACE_CLEAN(SCOPE, CSCOPE)
      LS = TRACE_LENTRIM(CSCOPE)
      IF(LS.LE.0) LS = 1
C
      CALL TRACE_REALHEX(SVAL, SVALHEX)
      CALL TRACE_REALHEX(DIWALLDPOST, DIWALLDPOSTHEX)
      CALL TRACE_REALHEX(DDHSVAL, DDHSHEX)
      CALL TRACE_REALHEX(HSHKVAL, HSHKHEX)
      CALL TRACE_REALHEX(HKDVAL, HKDHEX)
      CALL TRACE_REALHEX(HSDVAL, HSDHEX)
      CALL TRACE_REALHEX(DDUSVAL, DDUSHEX)
      CALL TRACE_REALHEX(USDVAL, USDHEX)
      CALL TRACE_REALHEX(DDDVAL, DDDHEX)
      CALL TRACE_REALHEX(DDLHSVAL, DDLHSHEX)
      CALL TRACE_REALHEX(DDLUSVAL, DDLUSHEX)
      CALL TRACE_REALHEX(DDLDVAL, DDLDHEX)
      CALL TRACE_REALHEX(FINALDIDVAL, FINALDIDHEX)
C
      SEQ = TRACE_NEXTSEQ()
      WRITE(51,1000) SEQ, CSCOPE(1:LS),
     &     SVAL, SVALHEX,
     &     DIWALLDPOST, DIWALLDPOSTHEX,
     &     DDHSVAL, DDHSHEX, HSHKVAL, HSHKHEX, HKDVAL, HKDHEX,
     &     HSDVAL, HSDHEX,
     &     DDUSVAL, DDUSHEX, USDVAL, USDHEX,
     &     DDDVAL, DDDHEX,
     &     DDLHSVAL, DDLHSHEX, DDLUSVAL, DDLUSHEX,
     &     DDLDVAL, DDLDHEX, FINALDIDVAL, FINALDIDHEX
 1000 FORMAT('{"sequence":',I12,',"runtime":"fortran",'
     & '"kind":"blvar_turbulent_d_update_terms","scope":"',A,
     & '","name":null,"data":{"s":',1PE24.16,
     & ',"sBits":"0x',A,'"',
     & ',"diWallDPostDfac":',1PE24.16,
     & ',"diWallDPostDfacBits":"0x',A,'"',
     & ',"ddHs":',1PE24.16,',"ddHsBits":"0x',A,'"',
     & ',"hsHk":',1PE24.16,',"hsHkBits":"0x',A,'"',
     & ',"hkD":',1PE24.16,',"hkDBits":"0x',A,'"',
     & ',"hsD":',1PE24.16,',"hsDBits":"0x',A,'"',
     & ',"ddUs":',1PE24.16,',"ddUsBits":"0x',A,'"',
     & ',"usD":',1PE24.16,',"usDBits":"0x',A,'"',
     & ',"ddD":',1PE24.16,',"ddDBits":"0x',A,'"',
     & ',"ddlHs":',1PE24.16,',"ddlHsBits":"0x',A,'"',
     & ',"ddlUs":',1PE24.16,',"ddlUsBits":"0x',A,'"',
     & ',"ddlD":',1PE24.16,',"ddlDBits":"0x',A,'"',
     & ',"finalDiD":',1PE24.16,',"finalDiDBits":"0x',A,'"',
     & '},"values":null,"tags":null,"timestampUtc":null}')
      RETURN
      END


      SUBROUTINE TRACE_BLVAR_TURBULENT_DI_TERMS(SCOPE,
     &    SVAL, HKVAL, HSVAL, USVAL, RTVAL,
     &    CF2TVAL, CF2THKVAL, CF2TRTVAL, CF2TMVAL, CF2TDVAL,
     &    DIWALLRAW, DIWALLHS, DIWALLUS, DIWALLCF, DIWALLDPRE,
     &    GRTVAL, HMINVAL, HMRTVAL, FLVAL, DFACVAL, DFHKVAL,
     &    DFRTVAL, DFTERMDVAL, DIWALLDPOST,
     &    DDVAL, DDHSVAL, DDUSVAL, DDDVAL,
     &    DDLVAL, DDLHSVAL, DDLUSVAL, DDLRTVAL, DDLDVAL,
     &    DILVAL, DILHKVAL, DILRTVAL, DILUSEDVAL, FINALDIVAL,
     &    FINALDIDVAL)
      CHARACTER*(*) SCOPE
      INTEGER TRACE_LENTRIM, TRACE_NEXTSEQ
      INTEGER LS, SEQ
      REAL SVAL, HKVAL, HSVAL, USVAL, RTVAL
      REAL CF2TVAL, CF2THKVAL, CF2TRTVAL, CF2TMVAL, CF2TDVAL
      REAL DIWALLRAW, DIWALLHS, DIWALLUS, DIWALLCF, DIWALLDPRE
      REAL GRTVAL, HMINVAL, HMRTVAL, FLVAL, DFACVAL, DFHKVAL
      REAL DFRTVAL, DFTERMDVAL, DIWALLDPOST
      REAL DDVAL, DDHSVAL, DDUSVAL, DDDVAL
      REAL DDLVAL, DDLHSVAL, DDLUSVAL, DDLRTVAL, DDLDVAL
      REAL DILVAL, DILHKVAL, DILRTVAL, DILUSEDVAL, FINALDIVAL
      REAL FINALDIDVAL
      LOGICAL LOPEN
      CHARACTER*128 CSCOPE
      CHARACTER*8 SVALHEX, HKVALHEX, RTVALHEX, DIWALLDPOSTHEX
      CHARACTER*8 DDDVALHEX, DDLDVALHEX, FINALDIHEX, FINALDIDHEX
C
      INQUIRE(UNIT=51, OPENED=LOPEN)
      IF(.NOT.LOPEN) RETURN
      CALL TRACE_CLEAN(SCOPE, CSCOPE)
      LS = TRACE_LENTRIM(CSCOPE)
      IF(LS.LE.0) LS = 1
C
      CALL TRACE_REALHEX(SVAL, SVALHEX)
      CALL TRACE_REALHEX(HKVAL, HKVALHEX)
      CALL TRACE_REALHEX(RTVAL, RTVALHEX)
      CALL TRACE_REALHEX(DIWALLDPOST, DIWALLDPOSTHEX)
      CALL TRACE_REALHEX(DDDVAL, DDDVALHEX)
      CALL TRACE_REALHEX(DDLDVAL, DDLDVALHEX)
      CALL TRACE_REALHEX(FINALDIVAL, FINALDIHEX)
      CALL TRACE_REALHEX(FINALDIDVAL, FINALDIDHEX)
C
      SEQ = TRACE_NEXTSEQ()
      WRITE(51,1000) SEQ, CSCOPE(1:LS),
     &     SVAL, SVALHEX, HKVAL, HKVALHEX, HSVAL, USVAL, RTVAL,
     &     RTVALHEX,
     &     CF2TVAL, CF2THKVAL, CF2TRTVAL, CF2TMVAL, CF2TDVAL,
     &     DIWALLRAW, DIWALLHS, DIWALLUS, DIWALLCF, DIWALLDPRE,
     &     GRTVAL, HMINVAL, HMRTVAL, FLVAL, DFACVAL, DFHKVAL,
     &     DFRTVAL, DFTERMDVAL, DIWALLDPOST, DIWALLDPOSTHEX,
     &     DDVAL, DDHSVAL, DDUSVAL, DDDVAL, DDDVALHEX,
     &     DDLVAL, DDLHSVAL, DDLUSVAL, DDLRTVAL, DDLDVAL,
     &     DDLDVALHEX, DILVAL, DILHKVAL, DILRTVAL, DILUSEDVAL,
     &     FINALDIVAL, FINALDIHEX, FINALDIDVAL, FINALDIDHEX
 1000 FORMAT('{"sequence":',I12,',"runtime":"fortran",'
     & '"kind":"blvar_turbulent_di_terms","scope":"',A,
     & '","name":null,"data":{"s":',1PE24.16,
     & ',"sBits":"0x',A,'"',
     & ',"hk":',1PE24.16,
     & ',"hkBits":"0x',A,'"',
     & ',"hs":',1PE24.16,',"us":',1PE24.16,',"rt":',1PE24.16,
     & ',"rtBits":"0x',A,'"',
     & ',"cf2t":',1PE24.16,',"cf2tHk":',1PE24.16,
     & ',"cf2tRt":',1PE24.16,',"cf2tM":',1PE24.16,
     & ',"cf2tD":',1PE24.16,
     & ',"diWallRaw":',1PE24.16,',"diWallHs":',1PE24.16,
     & ',"diWallUs":',1PE24.16,',"diWallCf":',1PE24.16,
     & ',"diWallDPreDfac":',1PE24.16,
     & ',"grt":',1PE24.16,',"hmin":',1PE24.16,
     & ',"hmRt":',1PE24.16,',"fl":',1PE24.16,
     & ',"dfac":',1PE24.16,',"dfHk":',1PE24.16,
     & ',"dfRt":',1PE24.16,',"dfTermD":',1PE24.16,
     & ',"diWallDPostDfac":',1PE24.16,
     & ',"diWallDPostDfacBits":"0x',A,'"',
     & ',"dd":',1PE24.16,',"ddHs":',1PE24.16,
     & ',"ddUs":',1PE24.16,',"ddD":',1PE24.16,
     & ',"ddDBits":"0x',A,'"',
     & ',"ddl":',1PE24.16,',"ddlHs":',1PE24.16,
     & ',"ddlUs":',1PE24.16,',"ddlRt":',1PE24.16,
     & ',"ddlD":',1PE24.16,
     & ',"ddlDBits":"0x',A,'"',
     & ',"dil":',1PE24.16,',"dilHk":',1PE24.16,
     & ',"dilRt":',1PE24.16,',"usedLaminar":',1PE24.16,
     & ',"finalDi":',1PE24.16,
     & ',"finalDiBits":"0x',A,'"',
     & ',"finalDiD":',1PE24.16,
     & ',"finalDiDBits":"0x',A,'"',
     & '},"values":null,"tags":null,"timestampUtc":null}')
      RETURN
      END


      SUBROUTINE TRACE_BLSYS_INTERVAL_INPUTS(SCOPE, SIDE, STATION,
     &    PHASE, ITYP,
     &    WAKE, TURB, TRAN, SIMI,
     &    X1VAL, X2VAL, U1VAL, U2VAL, T1VAL, T2VAL,
     &    D1VAL, D2VAL, S1VAL, S2VAL, DW1VAL, DW2VAL,
     &    AMPL1VAL, AMPL2VAL, M1VAL, M2VAL)
      CHARACTER*(*) SCOPE
      INTEGER TRACE_LENTRIM, TRACE_NEXTSEQ
      INTEGER LS, LP, SEQ, SIDE, STATION, PHASE, ITYP
      INTEGER IWAKE, ITURB, ITRAN, ISIMI
      REAL X1VAL, X2VAL, U1VAL, U2VAL, T1VAL, T2VAL
      REAL D1VAL, D2VAL, S1VAL, S2VAL, DW1VAL, DW2VAL
      REAL AMPL1VAL, AMPL2VAL, M1VAL, M2VAL
      LOGICAL WAKE, TURB, TRAN, SIMI, LOPEN
      CHARACTER*128 CSCOPE, CPHASE
      CHARACTER*5 CWAKE, CTURB, CTRAN, CSIMI
C
      INQUIRE(UNIT=51, OPENED=LOPEN)
      IF(.NOT.LOPEN) RETURN
      CALL TRACE_CLEAN(SCOPE, CSCOPE)
      LS = TRACE_LENTRIM(CSCOPE)
      CPHASE = 'unknown'
      IF(PHASE.EQ.1) CPHASE = 'setbl'
      IF(PHASE.EQ.2) CPHASE = 'mrchue'
      IF(PHASE.EQ.3) CPHASE = 'mrchdu'
      LP = TRACE_LENTRIM(CPHASE)
      IF(LS.LE.0) LS = 1
      IF(LP.LE.0) LP = 1
      IWAKE = 0
      IF(WAKE) IWAKE = 1
      ITURB = 0
      IF(TURB) ITURB = 1
      ITRAN = 0
      IF(TRAN) ITRAN = 1
      ISIMI = 0
      IF(SIMI) ISIMI = 1
      CWAKE = 'false'
      IF(IWAKE.EQ.1) CWAKE = 'true '
      CTURB = 'false'
      IF(ITURB.EQ.1) CTURB = 'true '
      CTRAN = 'false'
      IF(ITRAN.EQ.1) CTRAN = 'true '
      CSIMI = 'false'
      IF(ISIMI.EQ.1) CSIMI = 'true '
C
      SEQ = TRACE_NEXTSEQ()
      WRITE(51,1000) SEQ, CSCOPE(1:LS), SIDE, STATION,
     &               CPHASE(1:LP), ITYP,
     &               CWAKE, CTURB, CTRAN, CSIMI,
     &               X1VAL, X2VAL, U1VAL, U2VAL,
     &               T1VAL, T2VAL, D1VAL, D2VAL,
     &               S1VAL, S2VAL, DW1VAL, DW2VAL,
     &               AMPL1VAL, AMPL2VAL, M1VAL, M2VAL
 1000 FORMAT('{"sequence":',I12,',"runtime":"fortran",'
     & '"kind":"blsys_interval_inputs","scope":"',A,
     & '","name":null,"data":{"side":',I8,',"station":',I8,
     & ',"phase":"',A,'","ityp":',I2,
     & ',"wake":',A,',"turb":',A,',"tran":',A,',"simi":',A,
     & ',"x1":',1PE24.16,',"x2":',1PE24.16,
     & ',"u1":',1PE24.16,',"u2":',1PE24.16,
     & ',"t1":',1PE24.16,',"t2":',1PE24.16,
     & ',"d1":',1PE24.16,',"d2":',1PE24.16,
     & ',"s1":',1PE24.16,',"s2":',1PE24.16,
     & ',"dw1":',1PE24.16,',"dw2":',1PE24.16,
     & ',"ampl1":',1PE24.16,',"ampl2":',1PE24.16,
     & ',"m1":',1PE24.16,',"m2":',1PE24.16,
     & '},"values":null,"tags":null,"timestampUtc":null}')
      RETURN
      END


      SUBROUTINE TRACE_BLDIF_LOG_INPUTS(SCOPE, SIDE, STATION, PHASE,
     &    ITYP,
     &    X1VAL, X2VAL, U1VAL, U2VAL, T1VAL, T2VAL, HS1VAL, HS2VAL,
     &    XRATIOVAL, URATIOVAL, TRATIOVAL, HRATIOVAL)
      CHARACTER*(*) SCOPE
      INTEGER TRACE_LENTRIM, TRACE_NEXTSEQ
      INTEGER LS, LP, SEQ, SIDE, STATION, PHASE, ITYP
      INTEGER X1BITS, X2BITS, U1BITS, U2BITS
      INTEGER T1BITS, T2BITS, HS1BITS, HS2BITS
      INTEGER XRATIOBITS, URATIOBITS, TRATIOBITS, HRATIOBITS
      REAL X1VAL, X2VAL, U1VAL, U2VAL, T1VAL, T2VAL, HS1VAL, HS2VAL
      REAL XRATIOVAL, URATIOVAL, TRATIOVAL, HRATIOVAL
      REAL X1TMP, X2TMP, U1TMP, U2TMP
      REAL T1TMP, T2TMP, HS1TMP, HS2TMP
      REAL XRATIOTMP, URATIOTMP, TRATIOTMP, HRATIOTMP
      LOGICAL LOPEN
      CHARACTER*128 CSCOPE, CPHASE
C
      EQUIVALENCE (X1TMP, X1BITS), (X2TMP, X2BITS)
      EQUIVALENCE (U1TMP, U1BITS), (U2TMP, U2BITS)
      EQUIVALENCE (T1TMP, T1BITS), (T2TMP, T2BITS)
      EQUIVALENCE (HS1TMP, HS1BITS), (HS2TMP, HS2BITS)
      EQUIVALENCE (XRATIOTMP, XRATIOBITS), (URATIOTMP, URATIOBITS)
      EQUIVALENCE (TRATIOTMP, TRATIOBITS), (HRATIOTMP, HRATIOBITS)
C
      INQUIRE(UNIT=51, OPENED=LOPEN)
      IF(.NOT.LOPEN) RETURN
      CALL TRACE_CLEAN(SCOPE, CSCOPE)
      LS = TRACE_LENTRIM(CSCOPE)
      CPHASE = 'unknown'
      IF(PHASE.EQ.1) CPHASE = 'setbl'
      IF(PHASE.EQ.2) CPHASE = 'mrchue'
      IF(PHASE.EQ.3) CPHASE = 'mrchdu'
      LP = TRACE_LENTRIM(CPHASE)
      IF(LS.LE.0) LS = 1
      IF(LP.LE.0) LP = 1
C
      X1TMP = X1VAL
      X2TMP = X2VAL
      U1TMP = U1VAL
      U2TMP = U2VAL
      T1TMP = T1VAL
      T2TMP = T2VAL
      HS1TMP = HS1VAL
      HS2TMP = HS2VAL
      XRATIOTMP = XRATIOVAL
      URATIOTMP = URATIOVAL
      TRATIOTMP = TRATIOVAL
      HRATIOTMP = HRATIOVAL
      SEQ = TRACE_NEXTSEQ()
      WRITE(51,1000) SEQ, CSCOPE(1:LS), SIDE, STATION,
     &               CPHASE(1:LP), ITYP,
     &               X1VAL, X2VAL, U1VAL, U2VAL,
     &               T1VAL, T2VAL, HS1VAL, HS2VAL,
     &               X1BITS, X2BITS, U1BITS, U2BITS,
     &               T1BITS, T2BITS, HS1BITS, HS2BITS,
     &               XRATIOVAL, URATIOVAL, TRATIOVAL, HRATIOVAL,
     &               XRATIOBITS, URATIOBITS, TRATIOBITS, HRATIOBITS
 1000 FORMAT('{"sequence":',I12,',"runtime":"fortran",'
     & '"kind":"bldif_log_inputs","scope":"',A,
     & '","name":null,"data":{"side":',I8,',"station":',I8,
     & ',"phase":"',A,'","ityp":',I2,
     & ',"x1":',1PE24.16,',"x2":',1PE24.16,
     & ',"u1":',1PE24.16,',"u2":',1PE24.16,
     & ',"t1":',1PE24.16,',"t2":',1PE24.16,
     & ',"hs1":',1PE24.16,',"hs2":',1PE24.16,
     & ',"x1Bits":',I12,',"x2Bits":',I12,
     & ',"u1Bits":',I12,',"u2Bits":',I12,
     & ',"t1Bits":',I12,',"t2Bits":',I12,
     & ',"hs1Bits":',I12,',"hs2Bits":',I12,
     & ',"xRatio":',1PE24.16,',"uRatio":',1PE24.16,
     & ',"tRatio":',1PE24.16,',"hRatio":',1PE24.16,
     & ',"xRatioBits":',I12,',"uRatioBits":',I12,
     & ',"tRatioBits":',I12,',"hRatioBits":',I12,
     & '},"values":null,"tags":null,"timestampUtc":null}')
      RETURN
      END


      SUBROUTINE TRACE_BLDIF_COMMON(SCOPE, ITYP, CFMVAL, UPWVAL,
     &    XLOGVAL, ULOGVAL, TLOGVAL, HLOGVAL, DDLOGVAL)
      CHARACTER*(*) SCOPE
      INTEGER TRACE_LENTRIM, TRACE_NEXTSEQ
      INTEGER LS, SEQ, ITYP
      INTEGER CFMBITS, UPWBITS, XLOGBITS, ULOGBITS
      INTEGER TLOGBITS, HLOGBITS, DDLOGBITS
      REAL CFMVAL, UPWVAL, XLOGVAL, ULOGVAL, TLOGVAL, HLOGVAL,
     &     DDLOGVAL
      REAL CFMTMP, UPWTMP, XLOGTMP, ULOGTMP, TLOGTMP, HLOGTMP
      REAL DDLOGTMP
      LOGICAL LOPEN
      CHARACTER*128 CSCOPE
C
      EQUIVALENCE (CFMTMP, CFMBITS), (UPWTMP, UPWBITS)
      EQUIVALENCE (XLOGTMP, XLOGBITS), (ULOGTMP, ULOGBITS)
      EQUIVALENCE (TLOGTMP, TLOGBITS), (HLOGTMP, HLOGBITS)
      EQUIVALENCE (DDLOGTMP, DDLOGBITS)
C
      INQUIRE(UNIT=51, OPENED=LOPEN)
      IF(.NOT.LOPEN) RETURN
      CALL TRACE_CLEAN(SCOPE, CSCOPE)
      LS = TRACE_LENTRIM(CSCOPE)
      IF(LS.LE.0) LS = 1
C
      CFMTMP = CFMVAL
      UPWTMP = UPWVAL
      XLOGTMP = XLOGVAL
      ULOGTMP = ULOGVAL
      TLOGTMP = TLOGVAL
      HLOGTMP = HLOGVAL
      DDLOGTMP = DDLOGVAL
      SEQ = TRACE_NEXTSEQ()
      WRITE(51,1000) SEQ, CSCOPE(1:LS), ITYP,
     &               CFMVAL, UPWVAL, XLOGVAL, ULOGVAL,
     &               TLOGVAL, HLOGVAL, DDLOGVAL,
     &               CFMBITS, UPWBITS, XLOGBITS, ULOGBITS,
     &               TLOGBITS, HLOGBITS, DDLOGBITS
 1000 FORMAT('{"sequence":',I12,',"runtime":"fortran",'
     & '"kind":"bldif_common","scope":"',A,
     & '","name":null,"data":{"ityp":',I2,
     & ',"cfm":',1PE24.16,',"upw":',1PE24.16,
     & ',"xlog":',1PE24.16,',"ulog":',1PE24.16,
     & ',"tlog":',1PE24.16,',"hlog":',1PE24.16,
     & ',"ddlog":',1PE24.16,
     & ',"cfmBits":',I12,',"upwBits":',I12,
     & ',"xlogBits":',I12,',"ulogBits":',I12,
     & ',"tlogBits":',I12,',"hlogBits":',I12,
     & ',"ddlogBits":',I12,
     & '},"values":null,"tags":null,"timestampUtc":null}')
      RETURN
      END


      SUBROUTINE TRACE_BLDIF_UPW_TERMS(SCOPE, ITYP,
     &    HK1VAL, HK2VAL,
     &    HK1T1VAL, HK1D1VAL, HK1U1VAL, HK1MSVAL,
     &    HK2T2VAL, HK2D2VAL, HK2U2VAL, HK2MSVAL,
     &    HLVAL, HLSQVAL, EHHVAL, UPWHLVAL, UPWHDVAL,
     &    UPWHK1VAL, UPWHK2VAL,
     &    UPWT1VAL, UPWD1VAL, UPWU1VAL, UPWT2VAL,
     &    UPWD2VAL, UPWU2VAL, UPWMSVAL)
      CHARACTER*(*) SCOPE
      INTEGER TRACE_LENTRIM, TRACE_NEXTSEQ
      INTEGER LS, SEQ, ITYP
      REAL HK1VAL, HK2VAL
      REAL HK1T1VAL, HK1D1VAL, HK1U1VAL, HK1MSVAL
      REAL HK2T2VAL, HK2D2VAL, HK2U2VAL, HK2MSVAL
      REAL HLVAL, HLSQVAL, EHHVAL, UPWHLVAL, UPWHDVAL
      REAL UPWHK1VAL, UPWHK2VAL
      REAL UPWT1VAL, UPWD1VAL, UPWU1VAL, UPWT2VAL
      REAL UPWD2VAL, UPWU2VAL, UPWMSVAL
      LOGICAL LOPEN
      CHARACTER*128 CSCOPE
C
      INQUIRE(UNIT=51, OPENED=LOPEN)
      IF(.NOT.LOPEN) RETURN
      CALL TRACE_CLEAN(SCOPE, CSCOPE)
      LS = TRACE_LENTRIM(CSCOPE)
      IF(LS.LE.0) LS = 1
C
      SEQ = TRACE_NEXTSEQ()
      WRITE(51,1000) SEQ, CSCOPE(1:LS), ITYP,
     &               HK1VAL, HK2VAL,
     &               HK1T1VAL, HK1D1VAL, HK1U1VAL, HK1MSVAL,
     &               HK2T2VAL, HK2D2VAL, HK2U2VAL, HK2MSVAL,
     &               HLVAL, HLSQVAL, EHHVAL, UPWHLVAL, UPWHDVAL,
     &               UPWHK1VAL, UPWHK2VAL,
     &               UPWT1VAL, UPWD1VAL, UPWU1VAL, UPWT2VAL,
     &               UPWD2VAL, UPWU2VAL, UPWMSVAL
 1000 FORMAT('{"sequence":',I12,',"runtime":"fortran",'
     & '"kind":"bldif_upw_terms","scope":"',A,
     & '","name":null,"data":{"ityp":',I2,
     & ',"hk1":',1PE24.16,',"hk2":',1PE24.16,
     & ',"hk1T1":',1PE24.16,',"hk1D1":',1PE24.16,
     & ',"hk1U1":',1PE24.16,',"hk1Ms":',1PE24.16,
     & ',"hk2T2":',1PE24.16,',"hk2D2":',1PE24.16,
     & ',"hk2U2":',1PE24.16,',"hk2Ms":',1PE24.16,
     & ',"hl":',1PE24.16,',"hlsq":',1PE24.16,
     & ',"ehh":',1PE24.16,',"upwHl":',1PE24.16,
     & ',"upwHd":',1PE24.16,',"upwHk1":',1PE24.16,
     & ',"upwHk2":',1PE24.16,',"upwT1":',1PE24.16,
     & ',"upwD1":',1PE24.16,',"upwU1":',1PE24.16,
     & ',"upwT2":',1PE24.16,',"upwD2":',1PE24.16,
     & ',"upwU2":',1PE24.16,',"upwMs":',1PE24.16,
     & '},"values":null,"tags":null,"timestampUtc":null}')
      RETURN
      END


      SUBROUTINE TRACE_BLDIF_Z_UPW_TERMS(SCOPE, ITYP,
     &    ZCQAVAL, CQDELTAVAL, CQTERMVAL,
     &    ZSAVAL, SDELTAVAL, STERMVAL,
     &    ZCFAVAL, CFDELTAVAL, CFTERMVAL,
     &    ZHKAVAL, HKDELTAVAL, HKTERMVAL,
     &    SUM12VAL, SUM123VAL, ZUPWVAL)
      CHARACTER*(*) SCOPE
      INTEGER TRACE_LENTRIM, TRACE_NEXTSEQ
      INTEGER LS, SEQ, ITYP
      REAL ZCQAVAL, CQDELTAVAL, CQTERMVAL
      REAL ZSAVAL, SDELTAVAL, STERMVAL
      REAL ZCFAVAL, CFDELTAVAL, CFTERMVAL
      REAL ZHKAVAL, HKDELTAVAL, HKTERMVAL
      REAL SUM12VAL, SUM123VAL, ZUPWVAL
      LOGICAL LOPEN
      CHARACTER*128 CSCOPE
C
      INQUIRE(UNIT=51, OPENED=LOPEN)
      IF(.NOT.LOPEN) RETURN
      CALL TRACE_CLEAN(SCOPE, CSCOPE)
      LS = TRACE_LENTRIM(CSCOPE)
      IF(LS.LE.0) LS = 1
C
      SEQ = TRACE_NEXTSEQ()
      WRITE(51,1000) SEQ, CSCOPE(1:LS), ITYP,
     &               ZCQAVAL, CQDELTAVAL, CQTERMVAL,
     &               ZSAVAL, SDELTAVAL, STERMVAL,
     &               ZCFAVAL, CFDELTAVAL, CFTERMVAL,
     &               ZHKAVAL, HKDELTAVAL, HKTERMVAL,
     &               SUM12VAL, SUM123VAL, ZUPWVAL
 1000 FORMAT('{"sequence":',I12,',"runtime":"fortran",'
     & '"kind":"bldif_z_upw_terms","scope":"',A,
     & '","name":null,"data":{"ityp":',I2,
     & ',"zCqA":',1PE24.16,',"cqDelta":',1PE24.16,
     & ',"cqTerm":',1PE24.16,',"zSa":',1PE24.16,
     & ',"sDelta":',1PE24.16,',"sTerm":',1PE24.16,
     & ',"zCfA":',1PE24.16,',"cfDelta":',1PE24.16,
     & ',"cfTerm":',1PE24.16,',"zHkA":',1PE24.16,
     & ',"hkDelta":',1PE24.16,',"hkTerm":',1PE24.16,
     & ',"sum12":',1PE24.16,',"sum123":',1PE24.16,
     & ',"zUpw":',1PE24.16,
     & '},"values":null,"tags":null,"timestampUtc":null}')
      RETURN
      END


      SUBROUTINE TRACE_BLDIF_RESIDUAL(SCOPE, SIDE, STATION, PHASE,
     &    ITYP, REZ1, REZ2, REZ3)
      CHARACTER*(*) SCOPE
      INTEGER TRACE_LENTRIM, TRACE_NEXTSEQ
      INTEGER LS, LP, SEQ, SIDE, STATION, PHASE, ITYP
      REAL REZ1, REZ2, REZ3
      LOGICAL LOPEN
      CHARACTER*128 CSCOPE, CPHASE
C
      INQUIRE(UNIT=51, OPENED=LOPEN)
      IF(.NOT.LOPEN) RETURN
      CALL TRACE_CLEAN(SCOPE, CSCOPE)
      LS = TRACE_LENTRIM(CSCOPE)
      CPHASE = 'unknown'
      IF(PHASE.EQ.1) CPHASE = 'setbl'
      IF(PHASE.EQ.2) CPHASE = 'mrchue'
      IF(PHASE.EQ.3) CPHASE = 'mrchdu'
      LP = TRACE_LENTRIM(CPHASE)
      IF(LS.LE.0) LS = 1
      IF(LP.LE.0) LP = 1
C
      SEQ = TRACE_NEXTSEQ()
      WRITE(51,1000) SEQ, CSCOPE(1:LS), SIDE, STATION,
     &               CPHASE(1:LP), ITYP,
     &               REZ1, REZ2, REZ3
 1000 FORMAT('{"sequence":',I12,',"runtime":"fortran",'
     & '"kind":"bldif_residual","scope":"',A,
     & '","name":null,"data":{"side":',I4,
     & ',"station":',I6,',"phase":"',A,'","ityp":',I2,
     & ',"rez1":',1PE24.16,',"rez2":',1PE24.16,
     & ',"rez3":',1PE24.16,
     & '},"values":null,"tags":null,"timestampUtc":null}')
      RETURN
      END


      SUBROUTINE TRACE_BLDIF_EQ3_RESIDUAL_TERMS(SCOPE, ITYP,
     &    HLOGVAL, BTMPVAL, ULOGVAL, BTMPULOGVAL, XLOGVAL, CFXVAL,
     &    HALFCFXVAL, DIXVAL, TRANSPORTVAL, XLOGTRANSPORTVAL,
     &    REZHVAL)
      CHARACTER*(*) SCOPE
      INTEGER TRACE_LENTRIM, TRACE_NEXTSEQ
      INTEGER LS, SEQ, ITYP
      REAL HLOGVAL, BTMPVAL, ULOGVAL, BTMPULOGVAL, XLOGVAL, CFXVAL,
     &     HALFCFXVAL, DIXVAL, TRANSPORTVAL, XLOGTRANSPORTVAL,
     &     REZHVAL
      LOGICAL LOPEN
      CHARACTER*128 CSCOPE
C
      INQUIRE(UNIT=51, OPENED=LOPEN)
      IF(.NOT.LOPEN) RETURN
      CALL TRACE_CLEAN(SCOPE, CSCOPE)
      LS = TRACE_LENTRIM(CSCOPE)
      IF(LS.LE.0) LS = 1
C
      SEQ = TRACE_NEXTSEQ()
      WRITE(51,1000) SEQ, CSCOPE(1:LS), ITYP,
     &               HLOGVAL, BTMPVAL, ULOGVAL, BTMPULOGVAL,
     &               XLOGVAL, CFXVAL, HALFCFXVAL, DIXVAL,
     &               TRANSPORTVAL, XLOGTRANSPORTVAL, REZHVAL
 1000 FORMAT('{"sequence":',I12,',"runtime":"fortran",'
     & '"kind":"bldif_eq3_residual_terms","scope":"',A,
     & '","name":null,"data":{"ityp":',I2,
     & ',"hlog":',1PE24.16,',"btmp":',1PE24.16,
     & ',"ulog":',1PE24.16,',"btmpUlog":',1PE24.16,
     & ',"xlog":',1PE24.16,',"cfx":',1PE24.16,
     & ',"halfCfx":',1PE24.16,',"dix":',1PE24.16,
     & ',"transport":',1PE24.16,',"xlogTransport":',1PE24.16,
     & ',"rezh":',1PE24.16,
     & '},"values":null,"tags":null,"timestampUtc":null}')
      RETURN
      END


      SUBROUTINE TRACE_CQ_DERIVATIVE_TERMS(SCOPE, ITYP,
     & HKVAL, HSVAL, USVAL, HVAL, RTVAL,
     & CQ_HS, CQ_US, CQ_HK, CQ_H, CQ_RT,
     & CQ_HK_T1, CQ_HK_T2, CQ_HK_T3,
     & TERM_HS_T, TERM_US_T, TERM_HK_T, TERM_H_T, TERM_RT_T,
     & TERM_HS_D, TERM_US_D, TERM_HK_D, TERM_H_D,
     & CQ_T, CQ_D)
      CHARACTER*(*) SCOPE
      INTEGER TRACE_LENTRIM, TRACE_NEXTSEQ
      INTEGER LS, SEQ, ITYP
      REAL HKVAL, HSVAL, USVAL, HVAL, RTVAL
      REAL CQ_HS, CQ_US, CQ_HK, CQ_H, CQ_RT
      REAL CQ_HK_T1, CQ_HK_T2, CQ_HK_T3
      REAL TERM_HS_T, TERM_US_T, TERM_HK_T, TERM_H_T, TERM_RT_T
      REAL TERM_HS_D, TERM_US_D, TERM_HK_D, TERM_H_D
      REAL CQ_T, CQ_D
      LOGICAL LOPEN
      CHARACTER*128 CSCOPE
C
      INQUIRE(UNIT=51, OPENED=LOPEN)
      IF(.NOT.LOPEN) RETURN
      CALL TRACE_CLEAN(SCOPE, CSCOPE)
      LS = TRACE_LENTRIM(CSCOPE)
      IF(LS.LE.0) LS = 1
C
      SEQ = TRACE_NEXTSEQ()
      WRITE(51,1000) SEQ, CSCOPE(1:LS), ITYP,
     & HKVAL, HSVAL, USVAL, HVAL, RTVAL,
     & CQ_HS, CQ_US, CQ_HK, CQ_H, CQ_RT,
     & CQ_HK_T1, CQ_HK_T2, CQ_HK_T3,
     & TERM_HS_T, TERM_US_T, TERM_HK_T, TERM_H_T, TERM_RT_T,
     & TERM_HS_D, TERM_US_D, TERM_HK_D, TERM_H_D,
     & CQ_T, CQ_D
 1000 FORMAT('{"sequence":',I12,',"runtime":"fortran",'
     & '"kind":"cq_derivative_terms","scope":"',A,
     & '","name":null,"data":{"ityp":',I2,
     & ',"hk":',1PE24.16,',"hs":',1PE24.16,
     & ',"us":',1PE24.16,',"h":',1PE24.16,
     & ',"rt":',1PE24.16,
     & ',"cqHs":',1PE24.16,',"cqUs":',1PE24.16,
     & ',"cqHk":',1PE24.16,',"cqH":',1PE24.16,
     & ',"cqRt":',1PE24.16,',"cqHkTerm1":',1PE24.16,
     & ',"cqHkTerm2":',1PE24.16,',"cqHkTerm3":',1PE24.16,
     & ',"cqTermHsT":',1PE24.16,
     & ',"cqTermUsT":',1PE24.16,',"cqTermHkT":',1PE24.16,
     & ',"cqTermHT":',1PE24.16,',"cqTermRtT":',1PE24.16,
     & ',"cqTermHsD":',1PE24.16,',"cqTermUsD":',1PE24.16,
     & ',"cqTermHkD":',1PE24.16,',"cqTermHD":',1PE24.16,
     & ',"cqT":',1PE24.16,',"cqD":',1PE24.16,
     & '},"values":null,"tags":null,"timestampUtc":null}')
      RETURN
      END


      SUBROUTINE TRACE_CQ_TERMS(SCOPE, ITYP, HKVAL, HSVAL, USVAL,
     &    HVAL, RTVAL, HKCVAL, HKBVAL, USBVAL, NUMVAL, DENVAL,
     &    RATIOVAL, CQVAL)
      CHARACTER*(*) SCOPE
      INTEGER TRACE_LENTRIM, TRACE_NEXTSEQ
      INTEGER LS, SEQ, ITYP
      REAL HKVAL, HSVAL, USVAL, HVAL, RTVAL, HKCVAL, HKBVAL,
     &     USBVAL, NUMVAL, DENVAL, RATIOVAL, CQVAL
      LOGICAL LOPEN
      CHARACTER*128 CSCOPE
C
      INQUIRE(UNIT=51, OPENED=LOPEN)
      IF(.NOT.LOPEN) RETURN
      CALL TRACE_CLEAN(SCOPE, CSCOPE)
      LS = TRACE_LENTRIM(CSCOPE)
      IF(LS.LE.0) LS = 1
C
      SEQ = TRACE_NEXTSEQ()
      WRITE(51,1000) SEQ, CSCOPE(1:LS), ITYP,
     &               HKVAL, HSVAL, USVAL, HVAL, RTVAL,
     &               HKCVAL, HKBVAL, USBVAL, NUMVAL, DENVAL,
     &               RATIOVAL, CQVAL
 1000 FORMAT('{"sequence":',I12,',"runtime":"fortran",'
     & '"kind":"cq_terms","scope":"',A,
     & '","name":null,"data":{"ityp":',I2,
     & ',"hk":',1PE24.16,',"hs":',1PE24.16,
     & ',"us":',1PE24.16,',"h":',1PE24.16,
     & ',"rt":',1PE24.16,',"hkc":',1PE24.16,
     & ',"hkb":',1PE24.16,',"usb":',1PE24.16,
     & ',"num":',1PE24.16,',"den":',1PE24.16,
     & ',"ratio":',1PE24.16,',"cq":',1PE24.16,
     & '},"values":null,"tags":null,"timestampUtc":null}')
      RETURN
      END

      SUBROUTINE TRACE_BLDIF_EQ3_T1_TERMS(SCOPE, SIDE, STATION, ITYP,
     &    X1VAL, X2VAL, T1VAL, T2VAL, U1VAL, U2VAL, UPWVAL,
     &    XOT1VAL, XOT2VAL, CF1VAL, CF2VAL, DI1VAL, DI2VAL,
     &    CF1XOT1VAL, CF2XOT2VAL, DI1XOT1VAL, DI2XOT2VAL,
     &    ZTERM_CF1VAL, ZTERM_DI1VAL, ZT1BODYVAL, ZT1WAKEVAL,
     &    ZHS1VAL, HS1T1VAL, ZCF1VAL, CF1T1VAL, ZDI1VAL, DI1T1VAL,
     &    BASEHS, BASECF, BASEDI, BASEZT, EXTRAH,
     &    ZCFXVAL, ZDIXVAL, CFXUPWVAL, DIXUPWVAL, ZUPWVAL,
     &    UPWTVAL, EXTRAUPW, BASESTORE, ROWVAL)
      CHARACTER*(*) SCOPE
      INTEGER TRACE_LENTRIM, TRACE_NEXTSEQ
      INTEGER LS, SEQ, SIDE, STATION, ITYP
      REAL X1VAL, X2VAL, T1VAL, T2VAL, U1VAL, U2VAL, UPWVAL,
     &     XOT1VAL, XOT2VAL, CF1VAL, CF2VAL, DI1VAL, DI2VAL,
     &     CF1XOT1VAL, CF2XOT2VAL, DI1XOT1VAL, DI2XOT2VAL,
     &     ZTERM_CF1VAL, ZTERM_DI1VAL, ZT1BODYVAL, ZT1WAKEVAL,
     &     ZHS1VAL, HS1T1VAL, ZCF1VAL, CF1T1VAL, ZDI1VAL, DI1T1VAL,
     &     BASEHS, BASECF, BASEDI, BASEZT, EXTRAH,
     &     ZCFXVAL, ZDIXVAL, CFXUPWVAL, DIXUPWVAL, ZUPWVAL,
     &     UPWTVAL, EXTRAUPW, BASESTORE, ROWVAL
      LOGICAL LOPEN
      CHARACTER*128 CSCOPE
C
      INQUIRE(UNIT=51, OPENED=LOPEN)
      IF(.NOT.LOPEN) RETURN
      CALL TRACE_CLEAN(SCOPE, CSCOPE)
      LS = TRACE_LENTRIM(CSCOPE)
      IF(LS.LE.0) LS = 1
C
      SEQ = TRACE_NEXTSEQ()
      WRITE(51,1000) SEQ, CSCOPE(1:LS), SIDE, STATION, ITYP,
     &               X1VAL, X2VAL, T1VAL, T2VAL, U1VAL, U2VAL, UPWVAL,
     &               XOT1VAL, XOT2VAL, CF1VAL, CF2VAL, DI1VAL, DI2VAL,
     &               CF1XOT1VAL, CF2XOT2VAL, DI1XOT1VAL, DI2XOT2VAL,
     &               ZTERM_CF1VAL, ZTERM_DI1VAL, ZT1BODYVAL, ZT1WAKEVAL,
     &               ZHS1VAL, HS1T1VAL, ZCF1VAL, CF1T1VAL, ZDI1VAL,
     &               DI1T1VAL,
     &               BASEHS, BASECF, BASEDI, BASEZT, EXTRAH,
     &               ZCFXVAL, ZDIXVAL, CFXUPWVAL, DIXUPWVAL, ZUPWVAL,
     &               UPWTVAL, EXTRAUPW, BASESTORE, ROWVAL
 1000 FORMAT('{"sequence":',I12,',"runtime":"fortran",'
     & '"kind":"bldif_eq3_t1_terms","scope":"',A,
     & '","name":null,"data":{"side":',I4,
     & ',"station":',I6,',"ityp":',I2,
     & ',"x1":',1PE24.16,',"x2":',1PE24.16,
     & ',"t1":',1PE24.16,',"t2":',1PE24.16,
     & ',"u1":',1PE24.16,',"u2":',1PE24.16,
     & ',"upw":',1PE24.16,',"xot1":',1PE24.16,
     & ',"xot2":',1PE24.16,',"cf1":',1PE24.16,
     & ',"cf2":',1PE24.16,',"di1":',1PE24.16,
     & ',"di2":',1PE24.16,',"cf1xot1":',1PE24.16,
     & ',"cf2xot2":',1PE24.16,',"di1xot1":',1PE24.16,
     & ',"di2xot2":',1PE24.16,',"zTermCf1":',1PE24.16,
     & ',"zTermDi1":',1PE24.16,',"zT1Body":',1PE24.16,
     & ',"zT1Wake":',1PE24.16,',"zHs1":',1PE24.16,
     & ',"hs1T1":',1PE24.16,',"zCf1":',1PE24.16,
     & ',"cf1T1":',1PE24.16,',"zDi1":',1PE24.16,
     & ',"di1T1":',1PE24.16,',"baseHs":',1PE24.16,
     & ',"baseCf":',1PE24.16,',"baseDi":',1PE24.16,
     & ',"baseZT":',1PE24.16,',"extraH":',1PE24.16,
     & ',"zCfx":',1PE24.16,',"zDix":',1PE24.16,
     & ',"cfxUpw":',1PE24.16,',"dixUpw":',1PE24.16,
     & ',"zUpw":',1PE24.16,',"upwT":',1PE24.16,
     & ',"extraUpw":',1PE24.16,',"baseStored32":',1PE24.16,
     & ',"row32":',1PE24.16,
     & '},"values":null,"tags":null,"timestampUtc":null}')
      RETURN
      END

      SUBROUTINE TRACE_BLDIF_EQ3_T2_TERMS(SCOPE, SIDE, STATION, ITYP,
     &    X1VAL, X2VAL, T1VAL, T2VAL, U1VAL, U2VAL, UPWVAL,
     &    XOT1VAL, XOT2VAL, CF1VAL, CF2VAL, DI1VAL, DI2VAL,
     &    CF1XOT1VAL, CF2XOT2VAL, DI1XOT1VAL, DI2XOT2VAL,
     &    ZTERM_CF2VAL, ZTERM_DI2VAL, ZT2BODYVAL, ZT2WAKEVAL,
     &    ZHS2VAL, HS2T2VAL, ZCF2VAL, CF2T2VAL, ZDI2VAL, DI2T2VAL,
     &    BASEHS, BASECF, BASEDI, BASEZT, EXTRAH,
     &    ZCFXVAL, ZDIXVAL, CFXUPWVAL, DIXUPWVAL, ZUPWVAL,
     &    UPWTVAL, EXTRAUPW, BASESTORE, ROWVAL)
      CHARACTER*(*) SCOPE
      INTEGER TRACE_LENTRIM, TRACE_NEXTSEQ
      INTEGER LS, SEQ, SIDE, STATION, ITYP
      REAL X1VAL, X2VAL, T1VAL, T2VAL, U1VAL, U2VAL, UPWVAL,
     &     XOT1VAL, XOT2VAL, CF1VAL, CF2VAL, DI1VAL, DI2VAL,
     &     CF1XOT1VAL, CF2XOT2VAL, DI1XOT1VAL, DI2XOT2VAL,
     &     ZTERM_CF2VAL, ZTERM_DI2VAL, ZT2BODYVAL, ZT2WAKEVAL,
     &     ZHS2VAL, HS2T2VAL, ZCF2VAL, CF2T2VAL, ZDI2VAL, DI2T2VAL,
     &     BASEHS, BASECF, BASEDI, BASEZT, EXTRAH,
     &     ZCFXVAL, ZDIXVAL, CFXUPWVAL, DIXUPWVAL, ZUPWVAL,
     &     UPWTVAL, EXTRAUPW, BASESTORE, ROWVAL
      LOGICAL LOPEN
      CHARACTER*128 CSCOPE
C
      INQUIRE(UNIT=51, OPENED=LOPEN)
      IF(.NOT.LOPEN) RETURN
      CALL TRACE_CLEAN(SCOPE, CSCOPE)
      LS = TRACE_LENTRIM(CSCOPE)
      IF(LS.LE.0) LS = 1
C
      SEQ = TRACE_NEXTSEQ()
      WRITE(51,1000) SEQ, CSCOPE(1:LS), SIDE, STATION, ITYP,
     &               X1VAL, X2VAL, T1VAL, T2VAL, U1VAL, U2VAL, UPWVAL,
     &               XOT1VAL, XOT2VAL, CF1VAL, CF2VAL, DI1VAL, DI2VAL,
     &               CF1XOT1VAL, CF2XOT2VAL, DI1XOT1VAL, DI2XOT2VAL,
     &               ZTERM_CF2VAL, ZTERM_DI2VAL, ZT2BODYVAL, ZT2WAKEVAL,
     &               ZHS2VAL, HS2T2VAL, ZCF2VAL, CF2T2VAL, ZDI2VAL,
     &               DI2T2VAL,
     &               BASEHS, BASECF, BASEDI, BASEZT, EXTRAH,
     &               ZCFXVAL, ZDIXVAL, CFXUPWVAL, DIXUPWVAL, ZUPWVAL,
     &               UPWTVAL, EXTRAUPW, BASESTORE, ROWVAL
 1000 FORMAT('{"sequence":',I12,',"runtime":"fortran",'
     & '"kind":"bldif_eq3_t2_terms","scope":"',A,
     & '","name":null,"data":{"side":',I4,
     & ',"station":',I6,',"ityp":',I2,
     & ',"x1":',1PE24.16,',"x2":',1PE24.16,
     & ',"t1":',1PE24.16,',"t2":',1PE24.16,
     & ',"u1":',1PE24.16,',"u2":',1PE24.16,
     & ',"upw":',1PE24.16,',"xot1":',1PE24.16,
     & ',"xot2":',1PE24.16,',"cf1":',1PE24.16,
     & ',"cf2":',1PE24.16,',"di1":',1PE24.16,
     & ',"di2":',1PE24.16,',"cf1xot1":',1PE24.16,
     & ',"cf2xot2":',1PE24.16,',"di1xot1":',1PE24.16,
     & ',"di2xot2":',1PE24.16,',"zTermCf2":',1PE24.16,
     & ',"zTermDi2":',1PE24.16,',"zT2Body":',1PE24.16,
     & ',"zT2Wake":',1PE24.16,',"zHs2":',1PE24.16,
     & ',"hs2T2":',1PE24.16,',"zCf2":',1PE24.16,
     & ',"cf2T2":',1PE24.16,',"zDi2":',1PE24.16,
     & ',"di2T2":',1PE24.16,',"baseHs":',1PE24.16,
     & ',"baseCf":',1PE24.16,',"baseDi":',1PE24.16,
     & ',"baseZT":',1PE24.16,',"extraH":',1PE24.16,
     & ',"zCfx":',1PE24.16,',"zDix":',1PE24.16,
     & ',"cfxUpw":',1PE24.16,',"dixUpw":',1PE24.16,
     & ',"zUpw":',1PE24.16,',"upwT":',1PE24.16,
     & ',"extraUpw":',1PE24.16,',"baseStored32":',1PE24.16,
     & ',"row32":',1PE24.16,
     & '},"values":null,"tags":null,"timestampUtc":null}')
      RETURN
      END

      SUBROUTINE TRACE_BLDIF_EQ3_D1_TERMS(SCOPE, ITYP,
     &    ZHS1VAL, HS1D1VAL, ZCF1VAL, CF1D1VAL, ZDI1VAL, DI1D1VAL,
     &    BASEHS, BASECF, BASEDI, EXTRAH, XOT1VAL, XOT2VAL,
     &    CF1VAL, CF2VAL, DI1VAL, DI2VAL, ZCFXVAL, ZDIXVAL,
     &    CFXUPWVAL, DIXUPWVAL, ZUPWVAL, UPWDVAL,
     &    EXTRAUPW, BASESTORE, ROWVAL)
      CHARACTER*(*) SCOPE
      INTEGER TRACE_LENTRIM, TRACE_NEXTSEQ
      INTEGER LS, SEQ, ITYP
      REAL ZHS1VAL, HS1D1VAL, ZCF1VAL, CF1D1VAL, ZDI1VAL, DI1D1VAL
      REAL BASEHS, BASECF, BASEDI, EXTRAH, XOT1VAL, XOT2VAL,
     &     CF1VAL, CF2VAL, DI1VAL, DI2VAL, ZCFXVAL, ZDIXVAL,
     &     CFXUPWVAL, DIXUPWVAL, ZUPWVAL, UPWDVAL,
     &     EXTRAUPW, BASESTORE, ROWVAL
      LOGICAL LOPEN
      CHARACTER*128 CSCOPE
C
      INQUIRE(UNIT=51, OPENED=LOPEN)
      IF(.NOT.LOPEN) RETURN
      CALL TRACE_CLEAN(SCOPE, CSCOPE)
      LS = TRACE_LENTRIM(CSCOPE)
      IF(LS.LE.0) LS = 1
C
      SEQ = TRACE_NEXTSEQ()
      WRITE(51,1000) SEQ, CSCOPE(1:LS), ITYP,
     &               ZHS1VAL, HS1D1VAL, ZCF1VAL, CF1D1VAL,
     &               ZDI1VAL, DI1D1VAL,
     &               BASEHS, BASECF, BASEDI, EXTRAH,
     &               XOT1VAL, XOT2VAL, CF1VAL, CF2VAL, DI1VAL, DI2VAL,
     &               ZCFXVAL, ZDIXVAL, CFXUPWVAL, DIXUPWVAL,
     &               ZUPWVAL, UPWDVAL, EXTRAUPW, BASESTORE, ROWVAL
 1000 FORMAT('{"sequence":',I12,',"runtime":"fortran",'
     & '"kind":"bldif_eq3_d1_terms","scope":"',A,
     & '","name":null,"data":{"ityp":',I2,
     & ',"zHs1":',1PE24.16,',"hs1D1":',1PE24.16,
     & ',"zCf1":',1PE24.16,',"cf1D1":',1PE24.16,
     & ',"zDi1":',1PE24.16,',"di1D1":',1PE24.16,
     & ',"baseHs":',1PE24.16,',"baseCf":',1PE24.16,
     & ',"baseDi":',1PE24.16,',"extraH":',1PE24.16,
     & ',"xot1":',1PE24.16,',"xot2":',1PE24.16,
     & ',"cf1":',1PE24.16,',"cf2":',1PE24.16,
     & ',"di1":',1PE24.16,',"di2":',1PE24.16,
     & ',"zCfx":',1PE24.16,',"zDix":',1PE24.16,
     & ',"cfxUpw":',1PE24.16,',"dixUpw":',1PE24.16,
     & ',"zUpw":',1PE24.16,',"upwD":',1PE24.16,
     & ',"extraUpw":',1PE24.16,',"baseStored33":',1PE24.16,
     & ',"row33":',1PE24.16,
     & '},"values":null,"tags":null,"timestampUtc":null}')
      RETURN
      END


      SUBROUTINE TRACE_BLDIF_EQ3_D2_TERMS(SCOPE, ITYP,
     &    BASEHS, BASECF, BASEDI, EXTRAH, XOT1VAL, XOT2VAL,
     &    CF1VAL, CF2VAL, DI1VAL, DI2VAL, ZCFXVAL, ZDIXVAL,
     &    CFXUPWVAL, DIXUPWVAL, ZUPWVAL, UPWDVAL,
     &    EXTRAUPW, BASESTORE, ROWVAL)
      CHARACTER*(*) SCOPE
      INTEGER TRACE_LENTRIM, TRACE_NEXTSEQ
      INTEGER LS, SEQ, ITYP
      REAL BASEHS, BASECF, BASEDI, EXTRAH, XOT1VAL, XOT2VAL,
     &     CF1VAL, CF2VAL, DI1VAL, DI2VAL, ZCFXVAL, ZDIXVAL,
     &     CFXUPWVAL, DIXUPWVAL, ZUPWVAL, UPWDVAL,
     &     EXTRAUPW, BASESTORE, ROWVAL
      LOGICAL LOPEN
      CHARACTER*128 CSCOPE
C
      INQUIRE(UNIT=51, OPENED=LOPEN)
      IF(.NOT.LOPEN) RETURN
      CALL TRACE_CLEAN(SCOPE, CSCOPE)
      LS = TRACE_LENTRIM(CSCOPE)
      IF(LS.LE.0) LS = 1
C
      SEQ = TRACE_NEXTSEQ()
      WRITE(51,1000) SEQ, CSCOPE(1:LS), ITYP,
     &               BASEHS, BASECF, BASEDI, EXTRAH,
     &               XOT1VAL, XOT2VAL, CF1VAL, CF2VAL, DI1VAL, DI2VAL,
     &               ZCFXVAL, ZDIXVAL, CFXUPWVAL, DIXUPWVAL,
     &               ZUPWVAL, UPWDVAL, EXTRAUPW, BASESTORE, ROWVAL
 1000 FORMAT('{"sequence":',I12,',"runtime":"fortran",'
     & '"kind":"bldif_eq3_d2_terms","scope":"',A,
     & '","name":null,"data":{"ityp":',I2,
     & ',"baseHs":',1PE24.16,',"baseCf":',1PE24.16,
     & ',"baseDi":',1PE24.16,',"extraH":',1PE24.16,
     & ',"xot1":',1PE24.16,',"xot2":',1PE24.16,
     & ',"cf1":',1PE24.16,',"cf2":',1PE24.16,
     & ',"di1":',1PE24.16,',"di2":',1PE24.16,
     & ',"zCfx":',1PE24.16,',"zDix":',1PE24.16,
     & ',"cfxUpw":',1PE24.16,',"dixUpw":',1PE24.16,
     & ',"zUpw":',1PE24.16,',"upwD":',1PE24.16,
     & ',"extraUpw":',1PE24.16,',"baseStored33":',1PE24.16,
     & ',"row33":',1PE24.16,
     & '},"values":null,"tags":null,"timestampUtc":null}')
      RETURN
      END


      SUBROUTINE TRACE_BLDIF_EQ3_U2_TERMS(SCOPE, ITYP,
     &    ZHS2VAL, HS2U2VAL, ZCF2VAL, CF2U2VAL, ZDI2VAL, DI2U2VAL,
     &    ZU2VAL, ZHCAHALF, HC2U2VAL, ZUPWVAL, UPWU2VAL,
     &    BASEHS, BASECF, BASEDI, BASEZU, EXTRAH, EXTRAUPW,
     &    BASESTORE, ROWVAL)
      CHARACTER*(*) SCOPE
      INTEGER TRACE_LENTRIM, TRACE_NEXTSEQ
      INTEGER LS, SEQ, ITYP
      REAL ZHS2VAL, HS2U2VAL, ZCF2VAL, CF2U2VAL, ZDI2VAL, DI2U2VAL
      REAL ZU2VAL, ZHCAHALF, HC2U2VAL, ZUPWVAL, UPWU2VAL
      REAL BASEHS, BASECF, BASEDI, BASEZU, EXTRAH, EXTRAUPW
      REAL BASESTORE, ROWVAL
      LOGICAL LOPEN
      CHARACTER*128 CSCOPE
C
      INQUIRE(UNIT=51, OPENED=LOPEN)
      IF(.NOT.LOPEN) RETURN
      CALL TRACE_CLEAN(SCOPE, CSCOPE)
      LS = TRACE_LENTRIM(CSCOPE)
      IF(LS.LE.0) LS = 1
C
      SEQ = TRACE_NEXTSEQ()
      WRITE(51,1000) SEQ, CSCOPE(1:LS), ITYP,
     &               ZHS2VAL, HS2U2VAL, ZCF2VAL, CF2U2VAL,
     &               ZDI2VAL, DI2U2VAL, ZU2VAL, ZHCAHALF,
     &               HC2U2VAL, ZUPWVAL, UPWU2VAL,
     &               BASEHS, BASECF, BASEDI, BASEZU, EXTRAH,
     &               EXTRAUPW, BASESTORE, ROWVAL
 1000 FORMAT('{"sequence":',I12,',"runtime":"fortran",'
     & '"kind":"bldif_eq3_u2_terms","scope":"',A,
     & '","name":null,"data":{"ityp":',I2,
     & ',"zHs2":',1PE24.16,',"hs2U2":',1PE24.16,
     & ',"zCf2":',1PE24.16,',"cf2U2":',1PE24.16,
     & ',"zDi2":',1PE24.16,',"di2U2":',1PE24.16,
     & ',"zU2":',1PE24.16,',"zHcaHalf":',1PE24.16,
     & ',"hc2U2":',1PE24.16,',"zUpw":',1PE24.16,
     & ',"upwU2":',1PE24.16,',"baseHs":',1PE24.16,
     & ',"baseCf":',1PE24.16,',"baseDi":',1PE24.16,
     & ',"baseZU":',1PE24.16,',"extraH":',1PE24.16,
     & ',"extraUpw":',1PE24.16,',"baseStored34":',1PE24.16,
     & ',"row34":',1PE24.16,
     & '},"values":null,"tags":null,"timestampUtc":null}')
      RETURN
      END


      SUBROUTINE TRACE_LAMINAR_DISSIPATION(SCOPE, HKVAL, RTVAL,
     &    HKBVAL, HKBSQVAL, DENVAL, RATIOVAL, NUMERVAL,
     &    DIVAL, DIHKVAL, DIRTVAL)
      CHARACTER*(*) SCOPE
      INTEGER TRACE_LENTRIM, TRACE_NEXTSEQ
      INTEGER LS, SEQ
      REAL HKVAL, RTVAL, HKBVAL, HKBSQVAL, DENVAL, RATIOVAL,
     &     NUMERVAL, DIVAL, DIHKVAL, DIRTVAL
      LOGICAL LOPEN
      CHARACTER*128 CSCOPE
C
      INQUIRE(UNIT=51, OPENED=LOPEN)
      IF(.NOT.LOPEN) RETURN
      CALL TRACE_CLEAN(SCOPE, CSCOPE)
      LS = TRACE_LENTRIM(CSCOPE)
      IF(LS.LE.0) LS = 1
C
      SEQ = TRACE_NEXTSEQ()
      WRITE(51,1000) SEQ, CSCOPE(1:LS), HKVAL, RTVAL,
     &               HKBVAL, HKBSQVAL, DENVAL, RATIOVAL, NUMERVAL,
     &               DIVAL, DIHKVAL, DIRTVAL
 1000 FORMAT('{"sequence":',I12,',"runtime":"fortran",'
     & '"kind":"laminar_dissipation","scope":"',A,
     & '","name":null,"data":{"hk":',1PE24.16,
     & ',"rt":',1PE24.16,',"hkb":',1PE24.16,
     & ',"hkbSq":',1PE24.16,',"den":',1PE24.16,
     & ',"ratio":',1PE24.16,',"numerator":',1PE24.16,
     & ',"di":',1PE24.16,
     & ',"diHk":',1PE24.16,',"diRt":',1PE24.16,
     & '},"values":null,"tags":null,"timestampUtc":null}')
      RETURN
      END


      SUBROUTINE TRACE_PREDICTED_EDGE_VELOCITY(SCOPE, IS, IBL,
     &    UINVVAL, AIRVAL, WAKEVAL, USAVVAL)
      CHARACTER*(*) SCOPE
      INTEGER TRACE_LENTRIM, TRACE_NEXTSEQ
      INTEGER LS, SEQ, IS, IBL
      REAL UINVVAL, AIRVAL, WAKEVAL, USAVVAL
      LOGICAL LOPEN
      CHARACTER*128 CSCOPE
C
      INQUIRE(UNIT=51, OPENED=LOPEN)
      IF(.NOT.LOPEN) RETURN
      CALL TRACE_CLEAN(SCOPE, CSCOPE)
      LS = TRACE_LENTRIM(CSCOPE)
      IF(LS.LE.0) LS = 1
C
      SEQ = TRACE_NEXTSEQ()
      WRITE(51,1000) SEQ, CSCOPE(1:LS), IS, IBL,
     &               UINVVAL, AIRVAL, WAKEVAL, USAVVAL
 1000 FORMAT('{"sequence":',I12,',"runtime":"fortran",'
     & '"kind":"predicted_edge_velocity","scope":"',A,
     & '","name":null,"data":{"side":',I2,
     & ',"station":',I4,',"ueInv":',1PE24.16,
     & ',"airfoilContribution":',1PE24.16,
     & ',"wakeContribution":',1PE24.16,
     & ',"predicted":',1PE24.16,
     & '},"values":null,"tags":null,"timestampUtc":null}')
      RETURN
      END


      SUBROUTINE TRACE_PREDICTED_EDGE_VELOCITY_TERM(SCOPE, IS, IBL,
     &    JS, JBL, IPAN, JPAN, VTII, VTIJ, DIJVAL, MASSVAL,
     &    UEMVAL, CONTRVAL, ISWAKE)
      CHARACTER*(*) SCOPE
      INTEGER TRACE_LENTRIM, TRACE_NEXTSEQ
      INTEGER LS, SEQ, IS, IBL, JS, JBL, IPAN, JPAN, ISWAKE
      REAL VTII, VTIJ, DIJVAL, MASSVAL, UEMVAL, CONTRVAL
      LOGICAL LOPEN
      CHARACTER*128 CSCOPE
C
      INQUIRE(UNIT=51, OPENED=LOPEN)
      IF(.NOT.LOPEN) RETURN
      CALL TRACE_CLEAN(SCOPE, CSCOPE)
      LS = TRACE_LENTRIM(CSCOPE)
      IF(LS.LE.0) LS = 1
C
      SEQ = TRACE_NEXTSEQ()
      WRITE(51,1000) SEQ, CSCOPE(1:LS),
     &               IS, IBL, JS, JBL, IPAN, JPAN,
     &               VTII, VTIJ, DIJVAL, MASSVAL,
     &               UEMVAL, CONTRVAL, ISWAKE
 1000 FORMAT('{"sequence":',I12,',"runtime":"fortran",'
     & '"kind":"predicted_edge_velocity_term","scope":"',A,
     & '","name":null,"data":{"side":',I2,
     & ',"station":',I4,',"sourceSide":',I2,
     & ',"sourceStation":',I4,',"iPan":',I4,
     & ',"jPan":',I4,',"vtiI":',1PE24.16,
     & ',"vtiJ":',1PE24.16,',"dij":',1PE24.16,
     & ',"mass":',1PE24.16,',"ueM":',1PE24.16,
     & ',"contribution":',1PE24.16,
     & ',"isWakeSource":',I1,
     & '},"values":null,"tags":null,"timestampUtc":null}')
      RETURN
      END


      SUBROUTINE TRACE_STAGNATION_CANDIDATE(SCOPE, INDEX,
     &    GAMLEFT, GAMRIGHT, SLEFT, SRIGHT, MAGVAL)
      CHARACTER*(*) SCOPE
      INTEGER TRACE_LENTRIM, TRACE_NEXTSEQ
      INTEGER LS, SEQ, INDEX
      REAL GAMLEFT, GAMRIGHT, SLEFT, SRIGHT, MAGVAL
      LOGICAL LOPEN
      CHARACTER*128 CSCOPE
C
      INQUIRE(UNIT=51, OPENED=LOPEN)
      IF(.NOT.LOPEN) RETURN
      CALL TRACE_CLEAN(SCOPE, CSCOPE)
      LS = TRACE_LENTRIM(CSCOPE)
      IF(LS.LE.0) LS = 1
C
      SEQ = TRACE_NEXTSEQ()
      WRITE(51,1000) SEQ, CSCOPE(1:LS), INDEX,
     &               GAMLEFT, GAMRIGHT, SLEFT, SRIGHT, MAGVAL
 1000 FORMAT('{"sequence":',I12,',"runtime":"fortran",'
     & '"kind":"stagnation_candidate","scope":"',A,
     & '","name":null,"data":{"index":',I4,
     & ',"gammaLeft":',1PE24.16,',"gammaRight":',1PE24.16,
     & ',"panelArcLeft":',1PE24.16,',"panelArcRight":',1PE24.16,
     & ',"magnitude":',1PE24.16,
     & '},"values":null,"tags":null,"timestampUtc":null}')
      RETURN
      END


      SUBROUTINE TRACE_STAGNATION_INTERPOLATION(SCOPE, INDEX,
     &    GAMLEFT, GAMRIGHT, DGAMVAL, DSVAL, SLEFT, SRIGHT,
     &    USELEFT)
      CHARACTER*(*) SCOPE
      INTEGER TRACE_LENTRIM, TRACE_NEXTSEQ
      INTEGER LS, SEQ, INDEX, USELEFT
      REAL GAMLEFT, GAMRIGHT, DGAMVAL, DSVAL, SLEFT, SRIGHT
      LOGICAL LOPEN
      CHARACTER*128 CSCOPE
C
      INQUIRE(UNIT=51, OPENED=LOPEN)
      IF(.NOT.LOPEN) RETURN
      CALL TRACE_CLEAN(SCOPE, CSCOPE)
      LS = TRACE_LENTRIM(CSCOPE)
      IF(LS.LE.0) LS = 1
C
      SEQ = TRACE_NEXTSEQ()
      WRITE(51,1000) SEQ, CSCOPE(1:LS), INDEX,
     &               GAMLEFT, GAMRIGHT, DGAMVAL, DSVAL,
     &               SLEFT, SRIGHT, USELEFT
 1000 FORMAT('{"sequence":',I12,',"runtime":"fortran",'
     & '"kind":"stagnation_interpolation","scope":"',A,
     & '","name":null,"data":{"index":',I4,
     & ',"gammaLeft":',1PE24.16,',"gammaRight":',1PE24.16,
     & ',"dgam":',1PE24.16,',"ds":',1PE24.16,
     & ',"panelArcLeft":',1PE24.16,',"panelArcRight":',1PE24.16,
     & ',"usedLeftNode":',I1,
     & '},"values":null,"tags":null,"timestampUtc":null}')
      RETURN
      END


      SUBROUTINE TRACE_STAGNATION_SPEED_WINDOW(SCOPE, INDEX,
     &    WINDOWSTART, V1, V2, V3, V4, V5, V6)
      CHARACTER*(*) SCOPE
      INTEGER TRACE_LENTRIM, TRACE_NEXTSEQ
      INTEGER LS, SEQ, INDEX, WINDOWSTART
      REAL V1, V2, V3, V4, V5, V6
      LOGICAL LOPEN
      CHARACTER*128 CSCOPE
C
      INQUIRE(UNIT=51, OPENED=LOPEN)
      IF(.NOT.LOPEN) RETURN
      CALL TRACE_CLEAN(SCOPE, CSCOPE)
      LS = TRACE_LENTRIM(CSCOPE)
      IF(LS.LE.0) LS = 1
C
      SEQ = TRACE_NEXTSEQ()
      WRITE(51,1000) SEQ, CSCOPE(1:LS), INDEX, WINDOWSTART,
     &               V1, V2, V3, V4, V5, V6
 1000 FORMAT('{"sequence":',I12,',"runtime":"fortran",'
     & '"kind":"array","scope":"',A,
     & '","name":"stagnation_speed_window","data":{"index":',I4,
     & ',"windowStart":',I4,
     & '},"values":[',1PE24.16,',',1PE24.16,',',1PE24.16,
     & ',',1PE24.16,',',1PE24.16,',',1PE24.16,
     & '],"tags":null,"timestampUtc":null}')
      RETURN
      END
