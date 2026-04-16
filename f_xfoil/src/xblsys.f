C***********************************************************************
C    Module:  xblsys.f
C 
C    Copyright (C) 2000 Mark Drela 
C 
C    This program is free software; you can redistribute it and/or modify
C    it under the terms of the GNU General Public License as published by
C    the Free Software Foundation; either version 2 of the License, or
C    (at your option) any later version.
C
C    This program is distributed in the hope that it will be useful,
C    but WITHOUT ANY WARRANTY; without even the implied warranty of
C    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
C    GNU General Public License for more details.
C
C    You should have received a copy of the GNU General Public License
C    along with this program; if not, write to the Free Software
C    Foundation, Inc., 675 Mass Ave, Cambridge, MA 02139, USA.
C***********************************************************************


      SUBROUTINE TRCHEK
C
C---- structured trace
      CALL TRACE_ENTER('TRCHEK')
C
C---- 1st-order amplification equation
cc      CALL TRCHEK1
C
C---- 2nd-order amplification equation
      CALL TRCHEK2
C
      CALL TRACE_EXIT('TRCHEK')
      RETURN
      END



      SUBROUTINE AXSET( HK1,    T1,    RT1,    A1,
     &                  HK2,    T2,    RT2,    A2,  ACRIT, IDAMPV,
     &           AX, AX_HK1, AX_T1, AX_RT1, AX_A1,
     &               AX_HK2, AX_T2, AX_RT2, AX_A2 )
C----------------------------------------------------------
C     Returns average amplification AX over interval 1..2
C----------------------------------------------------------
C
      INTEGER TRACE_SIDE, TRACE_STATION, TRACE_PHASE, TRACE_ITER
      INTEGER TRACE_OUTER
      COMMON/V_TRACE/ TRACE_SIDE, TRACE_STATION, TRACE_PHASE,
     &                 TRACE_ITER, TRACE_OUTER
cC==========================
cC---- 1st-order -- based on "1" quantities only
c      CALL DAMPL( HK1, T1, RT1, AX1, AX1_HK1, AX1_T1, AX1_RT1 )
c      AX2_HK2 = 0.0
c      AX2_T2  = 0.0
c      AX2_RT2 = 0.0
cC
c      AX1_A1 = 0.0
c      AX2_A2 = 0.0
cC
c      AX     = AX1
c      AX_AX1 = 1.0
c      AX_AX2 = 0.0
cC
c      ARG = MIN( 20.0*(ACRIT-A1) , 20.0 )
c      EXN    = EXP(-ARG)
c      EXN_A1 = 20.0*EXN
c      EXN_A2 = 0.
cC
c      DAX    = EXN   * 0.0004/T1
c      DAX_A1 = EXN_A1* 0.0004/T1
c      DAX_A2 = 0.
c      DAX_T1 = -DAX/T1
c      DAX_T2 = 0.
C
C==========================
C---- 2nd-order
      CALL TRACE_TRANSITION_SENSITIVITY_INPUTS('AXSET',
     &     HK1, T1, RT1, A1,
     &     HK2, T2, RT2, A2, ACRIT, IDAMPV)
      IF(IDAMPV.EQ.0) THEN
       CALL DAMPL( HK1, T1, RT1, AX1, AX1_HK1, AX1_T1, AX1_RT1 )
       CALL DAMPL( HK2, T2, RT2, AX2, AX2_HK2, AX2_T2, AX2_RT2 )
      ELSE
       CALL DAMPL2( HK1, T1, RT1, AX1, AX1_HK1, AX1_T1, AX1_RT1 )
       CALL DAMPL2( HK2, T2, RT2, AX2, AX2_HK2, AX2_T2, AX2_RT2 )
      ENDIF
C
CC---- simple-average version
C      AXA = 0.5*(AX1 + AX2)
C      IF(AXA .LE. 0.0) THEN
C       AXA = 0.0
C       AXA_AX1 = 0.0
C       AXA_AX2 = 0.0
C      ELSE
C       AXA_AX1 = 0.5
C       AXA_AX2 = 0.5
C      ENDIF
C
C---- rms-average version (seems a little better on coarse grids)
      AXSQ = 0.5*(AX1**2 + AX2**2)
      IF(AXSQ .LE. 0.0) THEN
       AXA = 0.0
       AXA_AX1 = 0.0
       AXA_AX2 = 0.0
      ELSE
       AXA = SQRT(AXSQ)
       AXA_AX1 = 0.5*AX1/AXA
       AXA_AX2 = 0.5*AX2/AXA
      ENDIF
      AX1SQDBG = AX1**2
      AX2SQDBG = AX2**2
      AXSUMDBG = AX1SQDBG + AX2SQDBG
C
C----- small additional term to ensure  dN/dx > 0  near  N = Ncrit
       ARG = MIN( 20.0*(ACRIT-0.5*(A1+A2)) , 20.0 )
       IF(ARG.LE.0.0) THEN
        EXN    = 1.0
CC      EXN_AC = 0.
        EXN_A1 = 0.
        EXN_A2 = 0.
       ELSE
        EXN    = EXP(-ARG)
CC      EXN_AC = -20.0    *EXN
        EXN_A1 =  20.0*0.5*EXN
        EXN_A2 =  20.0*0.5*EXN
       ENDIF
C
       DAX    = EXN    * 0.002/(T1+T2)
CC     DAX_AC = EXN_AC * 0.002/(T1+T2)
       DAX_A1 = EXN_A1 * 0.002/(T1+T2)
       DAX_A2 = EXN_A2 * 0.002/(T1+T2)
       DAX_T1 = -DAX/(T1+T2)
       DAX_T2 = -DAX/(T1+T2)
C
c
c        DAX    = 0.
c        DAX_A1 = 0.
c        DAX_A2 = 0.
c        DAX_AC = 0.
c        DAX_T1 = 0.
c        DAX_T2 = 0.
C==========================
C
      CALL TRACE_TRANSITION_AXSET_TERMS('AXSET',
     &     AX1, AX2, AX1SQDBG, AX2SQDBG, AXSUMDBG,
     &     AXSQ, AXA, AXA_AX1, AXA_AX2,
     &     ARG, EXN, DAX, DAX_A1, DAX_A2, DAX_T1, DAX_T2,
     &     AXA + DAX)
      IF(TRACE_SIDE.EQ.2
     &   .AND. TRACE_STATION.GE.2
     &   .AND. TRACE_STATION.LE.4
     &   .AND. TRACE_PHASE.EQ.1) THEN
        WRITE(0,'(A,I2,A,I3,A,Z8,A,Z8,A,Z8,A,Z8,A,Z8,A,Z8,A,Z8,A,Z8)')
     &   'F_AXSET1 IS=',TRACE_SIDE,' IBL=',TRACE_STATION,
     &   ' HK1=',TRANSFER(HK1,1),
     &   ' T1=',TRANSFER(T1,1),
     &   ' RT1=',TRANSFER(RT1,1),
     &   ' A1=',TRANSFER(A1,1),
     &   ' HK2=',TRANSFER(HK2,1),
     &   ' T2=',TRANSFER(T2,1),
     &   ' RT2=',TRANSFER(RT2,1),
     &   ' A2=',TRANSFER(A2,1)
        WRITE(0,'(A,I2,A,I3,A,Z8,A,Z8,A,Z8,'//
     &   'A,Z8,A,Z8,A,Z8,A,Z8,A,Z8,A,Z8,A,Z8)')
     &   'F_AXSET2 IS=',TRACE_SIDE,' IBL=',TRACE_STATION,
     &   ' AX1=',TRANSFER(AX1,1),
     &   ' AX2=',TRANSFER(AX2,1),
     &   ' AX1SQ=',TRANSFER(AX1SQDBG,1),
     &   ' AX2SQ=',TRANSFER(AX2SQDBG,1),
     &   ' AXSQ=',TRANSFER(AXSQ,1),
     &   ' AXA=',TRANSFER(AXA,1),
     &   ' ARG=',TRANSFER(ARG,1),
     &   ' EXN=',TRANSFER(EXN,1),
     &   ' DAX=',TRANSFER(DAX,1),
     &   ' AX=',TRANSFER(AXA + DAX,1)
      ENDIF
      AX     = AXA             + DAX
      IF(TRACE_SIDE.EQ.2 .AND. TRACE_STATION.GE.66 .AND. TRACE_STATION.LE.70
     &   .AND. TRACE_OUTER.EQ.33) THEN
        WRITE(0,'(A,I3,A,I2,4(1X,A,Z8.8))') 'F_AXSET_AH79 ibl=',TRACE_STATION,
     &   ' ph=',TRACE_PHASE,
     &   'A1=',TRANSFER(A1,1),'A2=',TRANSFER(A2,1),
     &   'AX=',TRANSFER(AX,1),'DAX=',TRANSFER(DAX,1)
      ENDIF
C
      AX_HK1 = AXA_AX1*AX1_HK1
      AX_T1  = AXA_AX1*AX1_T1  + DAX_T1
      AX_RT1 = AXA_AX1*AX1_RT1
      AX_A1  =                   DAX_A1
C
      AX_HK2 = AXA_AX2*AX2_HK2
      AX_T2  = AXA_AX2*AX2_T2  + DAX_T2
      AX_RT2 = AXA_AX2*AX2_RT2
      AX_A2  =                   DAX_A2
C
      CALL TRACE_TRANSITION_SENSITIVITIES('AXSET',
     &     AX, AX_HK1, AX_T1, AX_RT1, AX_A1,
     &     AX_HK2, AX_T2, AX_RT2, AX_A2)
C
      RETURN
      END


c      SUBROUTINE TRCHEK1
cC-------------------------------------------------
cC     Checks if transition occurs in the current
cC     interval 1..2  (IBL-1...IBL) on side IS.
cC
cC     Old first-order version. 
cC
cC     Growth rate is evaluated at the upstream 
cC     point "1". The discrete amplification 
cC     equation is
cC
cC       Ncrit - N(X1)     
cC       -------------  =  N'(X1)
cC          XT - X1        
cC
cC     which can be immediately solved for 
cC     the transition location XT.
cC-------------------------------------------------
c      INCLUDE 'XBL.INC'
cC
cC---- calculate AMPL2 value
c      CALL AXSET( HK1,    T1,    RT1, AMPL1,
c     &            HK2,    T2,    RT2, AMPL2,  AMCRIT, IDAMPV,
c     &     AX, AX_HK1, AX_T1, AX_RT1, AX_A1,
c     &         AX_HK2, AX_T2, AX_RT2, AX_A2 )
c      AMPL2 = AMPL1 + AX*(X2-X1)
cC
cC---- test for free or forced transition
c      TRFREE = AMPL2.GE.AMCRIT
c      TRFORC = XIFORC.GT.X1 .AND. XIFORC.LE.X2
cC
cC---- set transition interval flag
c      TRAN = TRFORC .OR. TRFREE
cC
cC---- if no transition yet, just return
c      IF(.NOT.TRAN) RETURN
cC
cC---- resolve if both forced and free transition
c      IF(TRFREE .AND. TRFORC) THEN
c       XT = (AMCRIT-AMPL1)/AX  +  X1
c       TRFORC = XIFORC .LT. XT
c       TRFREE = XIFORC .GE. XT
c      ENDIF
cC
c      IF(TRFORC) THEN
cC----- if forced transition, then XT is prescribed
c       XT = XIFORC
c       XT_A1 = 0.
c       XT_X1 = 0.
c       XT_T1 = 0.
c       XT_D1 = 0.
c       XT_U1 = 0.
c       XT_X2 = 0.
c       XT_T2 = 0.
c       XT_D2 = 0.
c       XT_U2 = 0.
c       XT_MS = 0.
c       XT_RE = 0.
c       XT_XF = 1.0
c      ELSE
cC----- if free transition, XT is related to BL variables
cC-     by the amplification equation
cC
c       XT    =  (AMCRIT-AMPL1)/AX     + X1
c       XT_AX = -(AMCRIT-AMPL1)/AX**2
cC
c       XT_A1 = -1.0/AX - (AMCRIT-AMPL1)/AX**2 * AX_A1
c       XT_X1 = 1.0
c       XT_T1 = XT_AX*(AX_HK1*HK1_T1 + AX_T1 + AX_RT1*RT1_T1)
c       XT_D1 = XT_AX*(AX_HK1*HK1_D1                        )
c       XT_U1 = XT_AX*(AX_HK1*HK1_U1         + AX_RT1*RT1_U1)
c       XT_X2 = 0.
c       XT_T2 = 0.
c       XT_D2 = 0.
c       XT_U2 = 0.
c       XT_MS = XT_AX*(AX_HK1*HK1_MS         + AX_RT1*RT1_MS)
c       XT_RE = XT_AX*(                        AX_RT1*RT1_RE)
c       XT_XF = 0.0
c      ENDIF
cC
c      RETURN
c      END
 
 
      SUBROUTINE TRCHEK2
C----------------------------------------------------------------
C     New second-order version:  December 1994.
C
C     Checks if transition occurs in the current interval X1..X2.
C     If transition occurs, then set transition location XT, and 
C     its sensitivities to "1" and "2" variables.  If no transition, 
C     set amplification AMPL2.
C
C
C     Solves the implicit amplification equation for N2:
C
C       N2 - N1     N'(XT,NT) + N'(X1,N1)
C       -------  =  ---------------------
C       X2 - X1               2
C
C     In effect, a 2-point central difference is used between
C     X1..X2 (no transition), or X1..XT (transition).  The switch
C     is done by defining XT,NT in the equation above depending
C     on whether N2 exceeds Ncrit.
C
C  If N2<Ncrit:  NT=N2    , XT=X2                  (no transition)
C
C  If N2>Ncrit:  NT=Ncrit , XT=(Ncrit-N1)/(N2-N1)  (transition)
C
C
C----------------------------------------------------------------
      INCLUDE 'XBL.INC'
      DATA DAEPS / 5.0E-5 /
CCC   DATA DAEPS / 1.0D-12 /
C
      CALL TRACE_ENTER('TRCHEK2')
C
C---- save variables and sensitivities at IBL ("2") for future restoration
      DO 5 ICOM=1, NCOM
        C2SAV(ICOM) = COM2(ICOM)
    5 CONTINUE
C
C---- calculate average amplification rate AX over X1..X2 interval
      AMPL2IN = AMPL2
      IF(.FALSE.) THEN
        WRITE(0,'(A,I2,A,I2,A,I1,A,I3,A,Z8,A,Z8,A,Z8,A,Z8)')
     &   'F_TRC_IN oi=',TRACE_OUTER,' ii=',TRACE_ITER,
     &   ' s=',TRACE_SIDE,' i=',TRACE_STATION,
     &   ' A1=',TRANSFER(AMPL1,1),
     &   ' A2=',TRANSFER(AMPL2,1),
     &   ' X1=',TRANSFER(X1,1),
     &   ' X2=',TRANSFER(X2,1)
        WRITE(0,'(A,Z8,A,Z8,A,Z8,A,Z8,A,Z8,A,Z8)')
     &   'F_TRC_IN2 HK1=',TRANSFER(HK1,1),
     &   ' T1=',TRANSFER(T1,1),
     &   ' RT1=',TRANSFER(RT1,1),
     &   ' HK2=',TRANSFER(HK2,1),
     &   ' T2=',TRANSFER(T2,1),
     &   ' RT2=',TRANSFER(RT2,1)
      ENDIF
      CALL AXSET( HK1,    T1,    RT1, AMPL1,
     &            HK2,    T2,    RT2, AMPL2,  AMCRIT, IDAMPV,
     &     AX, AX_HK1, AX_T1, AX_RT1, AX_A1,
     &         AX_HK2, AX_T2, AX_RT2, AX_A2 )
C
      IF(.FALSE.) THEN
        WRITE(0,'(A,I2,A,I1,A,I3,A,Z8)')
     &   'F_TRC_AX it=',TRACE_ITER,
     &   ' s=',TRACE_SIDE,' i=',TRACE_STATION,
     &   ' AX=',TRANSFER(AX,1)
      ENDIF
C---- set initial guess for iterate N2 (AMPL2) at X2
      AMPL2 = AMPL1 + AX*(X2-X1)
      CALL TRACE_TRANSITION_POINT_SEED('TRCHEK2',
     & X1, X2, X2-X1,
     & HK1, T1, RT1, AMPL1,
     & HK2, T2, RT2, AMPL2IN, AMCRIT, IDAMPV,
     & AX, AMPL2)
C
C---- solve implicit system for amplification AMPL2
      DO 100 ITAM=1, 30
C
C---- define weighting factors WF1,WF2 for defining "T" quantities from 1,2
C
      IF(AMPL2 .LE. AMCRIT) THEN
C------ there is no transition yet,  "T" is the same as "2"
        AMPLT    = AMPL2
        AMPLT_A2 = 1.0
        SFA    = 1.0
        SFA_A1 = 0.
        SFA_A2 = 0.
      ELSE
C------ there is transition in X1..X2, "T" is set from N1, N2
        AMPLT    = AMCRIT
        AMPLT_A2 = 0.
        SFA    = (AMPLT - AMPL1)/(AMPL2-AMPL1)
        SFA_A1 = ( SFA  - 1.0  )/(AMPL2-AMPL1)
        SFA_A2 = (      - SFA  )/(AMPL2-AMPL1)
      ENDIF
C
      IF(XIFORC.LT.X2) THEN
        SFX    = (XIFORC - X1 )/(X2-X1)
        SFX_X1 = (SFX    - 1.0)/(X2-X1)
        SFX_X2 = (       - SFX)/(X2-X1)
        SFX_XF =  1.0          /(X2-X1)
        WRITE(0,'(A,Z8,A,Z8,A,Z8)')
     &   'F_SFXXF XIFORC=',TRANSFER(XIFORC,1),
     &   ' X2=',TRANSFER(X2,1),
     &   ' SFX_XF=',TRANSFER(SFX_XF,1)
      ELSE
        SFX    = 1.0
        SFX_X1 = 0.
        SFX_X2 = 0.
        SFX_XF = 0.
      ENDIF
C
C---- set weighting factor from free or forced transition
      IF(TRACE_SIDE.EQ.1 .AND. TRACE_STATION.EQ.79) THEN
       WRITE(*,'(A,Z8,A,Z8,A,Z8,A,Z8,A,Z8)')
     &  'F_TRCHEK_WF SFA=',TRANSFER(SFA,1),
     &  ' SFX=',TRANSFER(SFX,1),
     &  ' SFX_XF=',TRANSFER(SFX_XF,1),
     &  ' XIFORC=',TRANSFER(XIFORC,1),
     &  ' X2=',TRANSFER(X2,1)
      ENDIF
C---- TRCHEK_WF trace for case 188 NACA 0009 a=-2 Nc=12
      IF(TRACE_SIDE.EQ.1 .AND. TRACE_STATION.EQ.78) THEN
       WRITE(0,'(A,Z8,A,Z8,A,Z8,A,Z8,A,Z8,A,Z8)')
     &  'F_TRCHEK_WF78 SFA=',TRANSFER(SFA,1),
     &  ' SFX=',TRANSFER(SFX,1),
     &  ' SFX_XF=',TRANSFER(SFX_XF,1),
     &  ' XIFORC=',TRANSFER(XIFORC,1),
     &  ' X1=',TRANSFER(X1,1),
     &  ' X2=',TRANSFER(X2,1)
      ENDIF
      IF(TRACE_SIDE.EQ.2 .AND. TRACE_STATION.EQ.66
     &   .AND. TRACE_OUTER.EQ.10 .AND. TRACE_ITER.EQ.2) THEN
        WRITE(0,'(A,5(1X,A,Z8.8))') 'F_TRCHK_N66',
     &   'AMCRIT=',TRANSFER(AMCRIT,1),
     &   'AMPL1=',TRANSFER(AMPL1,1),
     &   'AMPL2=',TRANSFER(AMPL2,1),
     &   'SFA=',TRANSFER(SFA,1),
     &   'SFA_A1=',TRANSFER(SFA_A1,1)
      ENDIF
      IF(SFA.LT.SFX) THEN
        WF2    = SFA
        WF2_A1 = SFA_A1
        WF2_A2 = SFA_A2
        WF2_X1 = 0.
        WF2_X2 = 0.
        WF2_XF = 0.
      ELSE
        WF2    = SFX
        WF2_A1 = 0.
        WF2_A2 = 0.
        WF2_X1 = SFX_X1
        WF2_X2 = SFX_X2
        WF2_XF = SFX_XF
      ENDIF
      WRITE(0,'(A,A,Z8,A,Z8,A,Z8,A,Z8,A,Z8,A,L1)')
     & 'F_WF2 ',
     & ' WF2=',TRANSFER(WF2,1),' WF2_A1=',TRANSFER(WF2_A1,1),
     & ' SFA=',TRANSFER(SFA,1),' SFX=',TRANSFER(SFX,1),
     & ' SFA_A1=',TRANSFER(SFA_A1,1),' branchSFA=',(SFA.LT.SFX)
C
C
C=====================
CC---- 1st-order (based on "1" quantites only, for testing)
C      WF2    = 0.0
C      WF2_A1 = 0.0
C      WF2_A2 = 0.0
C      WF2_X1 = 0.0
C      WF2_X2 = 0.0
C      WF2_XF = 0.0
C=====================
C
      WF1    = 1.0 - WF2
      WF1_A1 =     - WF2_A1
      WF1_A2 =     - WF2_A2
      WF1_X1 =     - WF2_X1
      WF1_X2 =     - WF2_X2
      WF1_XF =     - WF2_XF
C
C---- interpolate BL variables to XT
      XT    = X1*WF1    + X2*WF2
      TT    = T1*WF1    + T2*WF2
      DT    = D1*WF1    + D2*WF2
      UT    = U1*WF1    + U2*WF2
C
      XT_A2 = X1*WF1_A2 + X2*WF2_A2
      TT_A2 = T1*WF1_A2 + T2*WF2_A2
      DT_A2 = D1*WF1_A2 + D2*WF2_A2
      UT_A2 = U1*WF1_A2 + U2*WF2_A2
C
C---- temporarily set "2" variables from "T" for BLKIN
      X2 = XT
      T2 = TT
      D2 = DT
      U2 = UT
C
C---- calculate laminar secondary "T" variables HKT, RTT
      CALL BLKIN
C
      HKT    = HK2
      HKT_TT = HK2_T2
      HKT_DT = HK2_D2
      HKT_UT = HK2_U2
      HKT_MS = HK2_MS
C
      RTT    = RT2
      RTT_TT = RT2_T2
      RTT_UT = RT2_U2
      RTT_MS = RT2_MS
      RTT_RE = RT2_RE
C
C---- restore clobbered "2" variables, except for AMPL2
      AMSAVE = AMPL2
      DO 8 ICOM=1, NCOM
        COM2(ICOM) = C2SAV(ICOM)
 8    CONTINUE
      AMPL2 = AMSAVE
C
C---- calculate amplification rate AX over current X1-XT interval
      CALL AXSET( HK1,    T1,    RT1, AMPL1,
     &            HKT,    TT,    RTT, AMPLT,  AMCRIT, IDAMPV,
     &     AX, AX_HK1, AX_T1, AX_RT1, AX_A1,
     &         AX_HKT, AX_TT, AX_RTT, AX_AT )
C
C---- punch out early if there is no amplification here
      IF(AX .LE. 0.0) GO TO 101
C
C---- set sensitivity of AX(A2)
      AX_A2 = (AX_HKT*HKT_TT + AX_TT + AX_RTT*RTT_TT)*TT_A2
     &      + (AX_HKT*HKT_DT                        )*DT_A2
     &      + (AX_HKT*HKT_UT         + AX_RTT*RTT_UT)*UT_A2
     &      +  AX_AT                                 *AMPLT_A2
C
C---- residual for implicit AMPL2 definition (amplification equation)
      RES    = AMPL2 - AMPL1 - AX   *(X2-X1) 
      RES_A2 = 1.0           - AX_A2*(X2-X1)
C
      DA2 = -RES/RES_A2
C
      RLX = 1.0
      DXT = XT_A2*DA2
      IF(TRACE_SIDE.EQ.2 .AND. TRACE_STATION.EQ.24
     &   .AND. TRACE_ITER.EQ.4) THEN
       WRITE(0,'(A,I2,5(A,Z8))') 'F_TR2_INNER itam=',ITAM,
     &  ' AMPL2=',TRANSFER(AMPL2,1),
     &  ' AX=',TRANSFER(AX,1),
     &  ' RES=',TRANSFER(RES,1),
     &  ' DA2=',TRANSFER(DA2,1),
     &  ' WF2=',TRANSFER(WF2,1)
      ENDIF
C
      IF(RLX*ABS(DXT/(X2-X1)) .GT. 0.05) RLX = 0.05*ABS((X2-X1)/DXT)
      IF(RLX*ABS(DA2)         .GT. 1.0 ) RLX = 1.0 *ABS(   1.0 /DA2)
C
      CALL TRACE_TRANSITION_POINT_ITER('TRCHEK2', ITAM,
     & X1, X2, AMPL1, AMPL2, AMCRIT, AX, WF2, XT, TT, DT, UT,
     & RES, RES_A2, DA2, RLX)
      IF(TRACE_SIDE.EQ.2 .AND. TRACE_STATION.EQ.5
     &   .AND. TRACE_ITER.EQ.1) THEN
        WRITE(0,'(A,I2,8(1X,Z8.8))') 'G_TRIT', ITAM,
     &    TRANSFER(AMPL2,1), TRANSFER(AMPLT,1), TRANSFER(AX,1),
     &    TRANSFER(SFA,1), TRANSFER(RES,1), TRANSFER(RES_A2,1),
     &    TRANSFER(DA2,1), TRANSFER(RLX,1)
        WRITE(0,'(A,I2,5(1X,Z8.8))') 'G_TRXT', ITAM,
     &    TRANSFER(WF2,1), TRANSFER(XT,1), TRANSFER(TT,1),
     &    TRANSFER(DT,1), TRANSFER(UT,1)
      ENDIF
C
      IF(TRACE_OUTER.EQ.33) THEN
        WRITE(0,'(A,I3,A,I3,A,I2,5(1X,A,Z8.8))') 'F_TRC33 s=',TRACE_SIDE,
     &   ' ibl=',TRACE_STATION,' tam=',ITAM,
     &   'A1=',TRANSFER(AMPL1,1),'A2=',TRANSFER(AMPL2,1),
     &   'DA=',TRANSFER(DA2,1),'RLX=',TRANSFER(RLX,1),
     &   'AX=',TRANSFER(AX,1)
      ENDIF
C---- check if converged
      IF(ABS(DA2) .LT. DAEPS) GO TO 101
      IF((AMPL2.GT.AMCRIT .AND. AMPL2+RLX*DA2.LT.AMCRIT).OR.
     &   (AMPL2.LT.AMCRIT .AND. AMPL2+RLX*DA2.GT.AMCRIT)    ) THEN
C------ limited Newton step so AMPL2 doesn't step across AMCRIT either way
        AMPL2 = AMCRIT
      ELSE
C------ regular Newton step
        AMPL2 = AMPL2 + RLX*DA2
      ENDIF
C
 100  CONTINUE
      WRITE(*,*) 'TRCHEK2: N2 convergence failed.'
      WRITE(*,6700) X1, XT, X2, AMPL1, AMPLT, AMPL2, AX, DA2
 6700 FORMAT(1X,'x:', 3F9.5,'  N:',3F7.3,'  Nx:',F8.3,'   dN:',E10.3)
C
 101  CONTINUE
      IF(TRACE_SIDE.EQ.2 .AND. TRACE_STATION.EQ.24
     &   .AND. TRACE_ITER.EQ.4) THEN
       WRITE(0,'(A,6(A,Z8))') 'F_TR22_INSIDE',
     &  ' AMPL2=',TRANSFER(AMPL2,1),
     &  ' WF2=',TRANSFER(WF2,1),
     &  ' XT=',TRANSFER(XT,1),
     &  ' TT=',TRANSFER(TT,1),
     &  ' DT=',TRANSFER(DT,1),
     &  ' UT=',TRANSFER(UT,1)
       WRITE(0,'(A,4(A,Z8))') 'F_FINALAX',
     &  ' Ax=',TRANSFER(AX,1),
     &  ' Ax_TT=',TRANSFER(AX_TT,1),
     &  ' Ax_HKT=',TRANSFER(AX_HKT,1),
     &  ' Ax_RTT=',TRANSFER(AX_RTT,1)
      ENDIF
      IF(TRACE_SIDE.EQ.2 .AND. TRACE_STATION.EQ.5
     &   .AND. TRACE_ITER.EQ.1) THEN
        WRITE(0,'(A,4(1X,Z8.8))') 'G_TRACC',
     &    TRANSFER(AMPL2,1), TRANSFER(WF2,1),
     &    TRANSFER(XT,1), TRANSFER(AMPLT,1)
      ENDIF
C
C
C---- test for free or forced transition
      TRFREE = AMPL2 .GE. AMCRIT
      TRFORC = XIFORC.GT.X1 .AND. XIFORC.LE.X2
C
C---- set transition interval flag
      TRAN = TRFORC .OR. TRFREE
C
      IF(.NOT.TRAN) RETURN
C
C---- resolve if both forced and free transition
      IF(TRFREE .AND. TRFORC) THEN
       TRFORC = XIFORC .LT. XT
       TRFREE = XIFORC .GE. XT
      ENDIF
C
C---- TRCHEK branch trace for case 188 (side 1 station 78) and NACA 0003 (side 2 station 80)
      IF((TRACE_SIDE.EQ.1 .AND. TRACE_STATION.EQ.78)
     &   .OR. (TRACE_SIDE.EQ.2 .AND. TRACE_STATION.EQ.80)) THEN
       WRITE(0,'(A,I1,A,I3,A,L2,A,L2,A,Z8,A,Z8,A,Z8)')
     &  'F_TRBRANCH s=',TRACE_SIDE,' i=',TRACE_STATION,
     &  ' TRFORC=',TRFORC,
     &  ' TRFREE=',TRFREE,
     &  ' XT=',TRANSFER(REAL(XT),1),
     &  ' XIFORC=',TRANSFER(REAL(XIFORC),1),
     &  ' AMPL2=',TRANSFER(REAL(AMPL2),1)
      ENDIF
      IF(TRFORC) THEN
C----- if forced transition, then XT is prescribed,
C-     no sense calculating the sensitivities, since we know them...
       XT = XIFORC
       XT_A1 = 0.
       XT_X1 = 0.
       XT_T1 = 0.
       XT_D1 = 0.
       XT_U1 = 0.
       XT_X2 = 0.
       XT_T2 = 0.
       XT_D2 = 0.
       XT_U2 = 0.
       XT_MS = 0.
       XT_RE = 0.
       XT_XF = 1.0
       IF((TRACE_SIDE.EQ.1 .AND. TRACE_STATION.EQ.78)
     &    .OR. (TRACE_SIDE.EQ.2 .AND. TRACE_STATION.EQ.80)) THEN
        WRITE(0,'(A,I1,A,I3,A,Z8,A,Z8)')
     &   'F_FORCX_ENTER s=',TRACE_SIDE,' i=',TRACE_STATION,
     &   ' XT_X2=',TRANSFER(REAL(XT_X2),1),
     &   ' XT_XF=',TRANSFER(REAL(XT_XF),1)
       ENDIF
       RETURN
      ENDIF
C
C---- free transition ... set sensitivities of XT
C
C---- XT( X1 X2 A1 A2 XF ),  TT( T1 T2 A1 A2 X1 X2 XF),   DT( ...
CC    XT    = X1*WF1    + X2*WF2
CC    TT    = T1*WF1    + T2*WF2
CC    DT    = D1*WF1    + D2*WF2
CC    UT    = U1*WF1    + U2*WF2
C
      XT_X1 =    WF1
      TT_T1 =    WF1
      DT_D1 =    WF1
      UT_U1 =    WF1
C
      XT_X2 =                WF2
      TT_T2 =                WF2
      DT_D2 =                WF2
      UT_U2 =                WF2
C
      XT_A1 = X1*WF1_A1 + X2*WF2_A1
      TT_A1 = T1*WF1_A1 + T2*WF2_A1
      DT_A1 = D1*WF1_A1 + D2*WF2_A1
      UT_A1 = U1*WF1_A1 + U2*WF2_A1
      WRITE(0,'(A,A,Z8,A,Z8,A,Z8,A,Z8,A,Z8)')
     & 'F_TR_DER ',
     & ' XT_A1=',TRANSFER(XT_A1,1),' TT_A1=',TRANSFER(TT_A1,1),
     & ' DT_A1=',TRANSFER(DT_A1,1),' UT_A1=',TRANSFER(UT_A1,1),
     & ' XT=',TRANSFER(XT,1)
C
CC    XT_A2 = X1*WF1_A2 + X2*WF2_A2
CC    TT_A2 = T1*WF1_A2 + T2*WF2_A2
CC    DT_A2 = D1*WF1_A2 + D2*WF2_A2
CC    UT_A2 = U1*WF1_A2 + U2*WF2_A2
C
      XT_X1 = X1*WF1_X1 + X2*WF2_X1 + XT_X1
      TT_X1 = T1*WF1_X1 + T2*WF2_X1
      DT_X1 = D1*WF1_X1 + D2*WF2_X1
      UT_X1 = U1*WF1_X1 + U2*WF2_X1
C
      XT_X2 = X1*WF1_X2 + X2*WF2_X2 + XT_X2
      TT_X2 = T1*WF1_X2 + T2*WF2_X2
      DT_X2 = D1*WF1_X2 + D2*WF2_X2
      UT_X2 = U1*WF1_X2 + U2*WF2_X2
C
      XT_XF = X1*WF1_XF + X2*WF2_XF
      TT_XF = T1*WF1_XF + T2*WF2_XF
      DT_XF = D1*WF1_XF + D2*WF2_XF
      UT_XF = U1*WF1_XF + U2*WF2_XF
C
C---- at this point, AX = AX( HK1, T1, RT1, A1, HKT, TT, RTT, AT )
C
C---- set sensitivities of AX( T1 D1 U1 A1 T2 D2 U2 A2 MS RE )
      AXA1BAS = AX_A1
      AXT1BASM = AX_T1
      AX_T1 =  AX_HK1*HK1_T1 + AX_T1 + AX_RT1*RT1_T1
     &      + (AX_HKT*HKT_TT + AX_TT + AX_RTT*RTT_TT)*TT_T1
      AX_D1 =  AX_HK1*HK1_D1
     &      + (AX_HKT*HKT_DT                        )*DT_D1
      AX_U1 =  AX_HK1*HK1_U1         + AX_RT1*RT1_U1
     &      + (AX_HKT*HKT_UT         + AX_RTT*RTT_UT)*UT_U1
      AX_A1 =  AX_A1
     &      + (AX_HKT*HKT_TT + AX_TT + AX_RTT*RTT_TT)*TT_A1
     &      + (AX_HKT*HKT_DT                        )*DT_A1
     &      + (AX_HKT*HKT_UT         + AX_RTT*RTT_UT)*UT_A1
      AX_X1 = (AX_HKT*HKT_TT + AX_TT + AX_RTT*RTT_TT)*TT_X1
     &      + (AX_HKT*HKT_DT                        )*DT_X1
     &      + (AX_HKT*HKT_UT         + AX_RTT*RTT_UT)*UT_X1
C
      AX_T2 = (AX_HKT*HKT_TT + AX_TT + AX_RTT*RTT_TT)*TT_T2
      AX_D2 = (AX_HKT*HKT_DT                        )*DT_D2
      AX_U2 = (AX_HKT*HKT_UT         + AX_RTT*RTT_UT)*UT_U2
      AX_A2 =  AX_AT                                 *AMPLT_A2
     &      + (AX_HKT*HKT_TT + AX_TT + AX_RTT*RTT_TT)*TT_A2
     &      + (AX_HKT*HKT_DT                        )*DT_A2
     &      + (AX_HKT*HKT_UT         + AX_RTT*RTT_UT)*UT_A2
      IF(TRACE_SIDE.EQ.2 .AND. TRACE_STATION.EQ.66
     &   .AND. TRACE_OUTER.EQ.10 .AND. TRACE_ITER.EQ.2) THEN
       AXA2_AM = AX_AT*AMPLT_A2
       AXA2_TT = (AX_HKT*HKT_TT + AX_TT + AX_RTT*RTT_TT)*TT_A2
       AXA2_DT = (AX_HKT*HKT_DT)*DT_A2
       AXA2_UT = (AX_HKT*HKT_UT + AX_RTT*RTT_UT)*UT_A2
       WRITE(0,'(A,5(A,Z8))') 'F_AXA2_10_66',
     &  ' ax_A2=',TRANSFER(AX_A2,1),
     &  ' amplT=',TRANSFER(AXA2_AM,1),
     &  ' ttT=',TRANSFER(AXA2_TT,1),
     &  ' dtT=',TRANSFER(AXA2_DT,1),
     &  ' utT=',TRANSFER(AXA2_UT,1)
      ENDIF
      AX_X2 = (AX_HKT*HKT_TT + AX_TT + AX_RTT*RTT_TT)*TT_X2
     &      + (AX_HKT*HKT_DT                        )*DT_X2
     &      + (AX_HKT*HKT_UT         + AX_RTT*RTT_UT)*UT_X2
C
      AX_XF = (AX_HKT*HKT_TT + AX_TT + AX_RTT*RTT_TT)*TT_XF
     &      + (AX_HKT*HKT_DT                        )*DT_XF
     &      + (AX_HKT*HKT_UT         + AX_RTT*RTT_UT)*UT_XF
C
      AX_MS =  AX_HKT*HKT_MS         + AX_RTT*RTT_MS
     &      +  AX_HK1*HK1_MS         + AX_RT1*RT1_MS
      AX_RE =                          AX_RTT*RTT_RE
     &                               + AX_RT1*RT1_RE
C
C
C---- set sensitivities of residual RES
CCC   RES  = AMPL2 - AMPL1 - AX*(X2-X1)
      Z_AX =               -    (X2-X1)
C
      Z_A1 = Z_AX*AX_A1 - 1.0
      Z_T1 = Z_AX*AX_T1
      Z_D1 = Z_AX*AX_D1
      Z_U1 = Z_AX*AX_U1
      Z_X1 = Z_AX*AX_X1 + AX
C
      Z_A2 = Z_AX*AX_A2 + 1.0
      Z_T2 = Z_AX*AX_T2
      Z_D2 = Z_AX*AX_D2
      Z_U2 = Z_AX*AX_U2
      Z_X2 = Z_AX*AX_X2 - AX
C
      Z_XF = Z_AX*AX_XF
      Z_MS = Z_AX*AX_MS
      Z_RE = Z_AX*AX_RE
C
C---- set sensitivities of XT, with RES being stationary for A2 constraint
      XTA1BAS = XT_A1
      XTA1BAS1 = X1*WF1_A1
      XTA1BAS2 = X2*WF2_A1
      XTA1COR = (XT_A2/Z_A2)*Z_A1
      XTX2BAS = XT_X2
      XTX2COR = (XT_A2/Z_A2)*Z_X2
      IF(TRACE_SIDE.EQ.2 .AND. TRACE_STATION.EQ.66
     &   .AND. TRACE_OUTER.EQ.10 .AND. TRACE_ITER.EQ.2) THEN
        WRITE(0,'(A,8(1X,A,Z8.8))') 'F_XTA1COR_N66',
     &   'AX=',TRANSFER(AX,1),
     &   'DX=',TRANSFER(X2-X1,1),
     &   'AX_A1=',TRANSFER(AX_A1,1),
     &   'Z_AX=',TRANSFER(Z_AX,1),
     &   'Z_A1=',TRANSFER(Z_A1,1),
     &   'Z_A2=',TRANSFER(Z_A2,1),
     &   'XT_A1_pre=',TRANSFER(XT_A1,1),
     &   'XT_A2=',TRANSFER(XT_A2,1)
      ENDIF
      XT_A1 = XT_A1 - XTA1COR
      XT_T1 =       - (XT_A2/Z_A2)*Z_T1
      XT_D1 =       - (XT_A2/Z_A2)*Z_D1
      XT_U1 =       - (XT_A2/Z_A2)*Z_U1
      XT_X1 = XT_X1 - (XT_A2/Z_A2)*Z_X1
      WRITE(0,'(A,A,Z8,A,Z8,A,Z8,A,Z8)')
     & 'F_TR_DER_POST ',
     & ' XT_A1=',TRANSFER(XT_A1,1),
     & ' TT_A1=',TRANSFER(TT_A1,1),
     & ' DT_A1=',TRANSFER(DT_A1,1),
     & ' UT_A1=',TRANSFER(UT_A1,1)
      XT_T2 =       - (XT_A2/Z_A2)*Z_T2
      XT_D2 =       - (XT_A2/Z_A2)*Z_D2
      XT_U2 =       - (XT_A2/Z_A2)*Z_U2
      XT_X2 = XT_X2 - (XT_A2/Z_A2)*Z_X2
C---- n6h20 Z derivative inputs trace at IBL=66 iter 2 mc=10
      IF(TRACE_SIDE.EQ.2 .AND. TRACE_STATION.EQ.66) THEN
       WRITE(0,'(A,6(A,Z8))') 'F_ZDERIV10_66',
     &  ' xtA2=',TRANSFER(XT_A2,1),
     &  ' zA2=',TRANSFER(Z_A2,1),
     &  ' zT2=',TRANSFER(Z_T2,1),
     &  ' zD2=',TRANSFER(Z_D2,1),
     &  ' zU2=',TRANSFER(Z_U2,1),
     &  ' zX2=',TRANSFER(Z_X2,1)
      ENDIF
      IF(TRACE_SIDE.EQ.2 .AND. TRACE_STATION.EQ.80) THEN
       WRITE(0,'(A,Z8,A,Z8,A,Z8,A,Z8,A,Z8)')
     &  'F_XTX2_80 bas=',TRANSFER(XTX2BAS,1),
     &  ' cor=',TRANSFER(XTX2COR,1),
     &  ' res=',TRANSFER(XT_X2,1),
     &  ' xtA2=',TRANSFER(XT_A2,1),
     &  ' zA2=',TRANSFER(Z_A2,1)
      ENDIF
      XT_MS =       - (XT_A2/Z_A2)*Z_MS
      XT_RE =       - (XT_A2/Z_A2)*Z_RE
      XT_XF = 0.0
      IF(.FALSE.) THEN
        WRITE(0,'(A,I2,A,I1,A,I3)')
     &   'F_TRC_OUT it=',TRACE_ITER,
     &   ' s=',TRACE_SIDE,' i=',TRACE_STATION
        WRITE(0,'(A,Z8,A,Z8,A,Z8,A,Z8)')
     &   'F_TRC_SEN XT_X2=',TRANSFER(XT_X2,1),
     &   ' XT_A1=',TRANSFER(XT_A1,1),
     &   ' XT_T1=',TRANSFER(XT_T1,1),
     &   ' XT_D1=',TRANSFER(XT_D1,1)
        WRITE(0,'(A,Z8,A,Z8,A,Z8,A,Z8)')
     &   'F_TRC_SEN2 WF2=',TRANSFER(WF2,1),
     &   ' A2=',TRANSFER(AMPL2,1),
     &   ' XT=',TRANSFER(XT,1),
     &   ' zA2=',TRANSFER(Z_A2,1)
      ENDIF
C
      AXTTCMB =  AX_HKT*HKT_TT + AX_TT + AX_RTT*RTT_TT
      AXDTCMB =  AX_HKT*HKT_DT
      AXUTCMB =  AX_HKT*HKT_UT + AX_RTT*RTT_UT
      AXA1TTM =  AXTTCMB*TT_A1
      AXA1DTM =  AXDTCMB*DT_A1
      AXA1UTM =  AXUTCMB*UT_A1
      AXA2ATM =  AX_AT*AMPLT_A2
      AXA2TTM =  AXTTCMB*TT_A2
      AXA2DTM =  AXDTCMB*DT_A2
      AXA2UTM =  AXUTCMB*UT_A2
      AXT1HKTM = AX_HK1*HK1_T1
      AXT1RTTM = AX_RT1*RT1_T1
      AXT1TTTM = AXTTCMB*TT_T1
      AXD1HKTM = AX_HK1*HK1_D1
      AXD1DTTM = AXDTCMB*DT_D1
C
      CALL TRACE_TRANSITION_FINAL_SENSITIVITIES('TRCHEK2',
     & X1, X2, T1, T2, D1, D2, U1, U2,
     & XT_A2, AMPLT_A2, WF2_A2, TT_A2, DT_A2, UT_A2,
     & AXTTCMB, AXDTCMB, AXUTCMB, AX_AT,
     & AXA1BAS, AXA1TTM, AXA1DTM, AXA1UTM,
     & AXA2ATM, AXA2TTM, AXA2DTM, AXA2UTM,
     & AXT1HKTM, AXT1BASM, AXT1RTTM, AXT1TTTM,
     & AX_T1, AXD1HKTM, AXD1DTTM, AX_D1, AX_U1, AX_A1, AX_X1,
     & AX_T2, AX_D2, AX_U2, AX_A2, AX_X2,
     & Z_A1, Z_T1, Z_D1, Z_U1, Z_X1,
     & Z_A2, Z_T2, Z_D2, Z_U2, Z_X2,
     & WF2_A1, XTA1BAS1, XTA1BAS2, XTA1BAS, XTA1COR,
     & XTX2BAS, XTX2COR,
     & XT_A1, XT_T1, XT_D1, XT_U1, XT_X1,
     & XT_T2, XT_D2, XT_U2, XT_X2)
C
      CALL TRACE_EXIT('TRCHEK2')
      RETURN
      END


      SUBROUTINE BLSYS(TRACEPHASE)
C------------------------------------------------------------------
C
C     Sets up the BL Newton system governing the current interval:
C
C     |       ||dA1|     |       ||dA2|       |     |
C     |  VS1  ||dT1|  +  |  VS2  ||dT2|   =   |VSREZ|
C     |       ||dD1|     |       ||dD2|       |     |
C              |dU1|              |dU2|
C              |dX1|              |dX2|
C
C        3x5    5x1         3x5    5x1          3x1
C
C     The system as shown corresponds to a laminar station
C     If TRAN, then  dS2  replaces  dA2
C     If TURB, then  dS1, dS2  replace  dA1, dA2
C
C------------------------------------------------------------------
      IMPLICIT REAL(M)
      INCLUDE 'XBL.INC'
C
      INTEGER ITYPB, TRACEPHASE
C
      CALL TRACE_ENTER('BLSYS')
C
      ITYPB = 1
      IF(WAKE) THEN
       ITYPB = 3
      ELSE IF(TURB.OR.TRAN) THEN
       ITYPB = 2
      ENDIF
C
C---- calculate secondary BL variables and their sensitivities
      IF(WAKE) THEN
       CALL BLVAR(3)
       CALL BLMID(3)
      ELSE IF(TURB.OR.TRAN) THEN
       CALL BLVAR(2)
       CALL BLMID(2)
      ELSE
       CALL BLVAR(1)
       CALL BLMID(1)
      ENDIF
C
C---- n ah79 station 2 side 1 kinematic iter 33
      IF(TRACE_SIDE.EQ.1 .AND. TRACE_STATION.EQ.2
     &   .AND. TRACE_OUTER.EQ.33) THEN
       WRITE(0,'(A,6(1X,A,Z8.8))') 'F_SEC_AH79 s=1 i=2',
     &  ' HK=',TRANSFER(HK2,1),' RT=',TRANSFER(RT2,1),
     &  ' HS=',TRANSFER(HS2,1),' US=',TRANSFER(US2,1),
     &  ' CF=',TRANSFER(CF2,1),' DI=',TRANSFER(DI2,1)
      ENDIF
C---- Trace kinematic2 at station 16 iter 1 MRCHDU
      IF(TRACE_SIDE.EQ.2 .AND. TRACE_STATION.EQ.16
     &   .AND. TRACE_ITER.EQ.1) THEN
       WRITE(*,'(A,6(1X,Z8.8))')
     &  'F_KIN2_16',
     &  TRANSFER(HK2,1),TRANSFER(HK2_T2,1),
     &  TRANSFER(HK2_D2,1),TRANSFER(H2,1),
     &  TRANSFER(RT2,1),TRANSFER(RT2_T2,1)
      ENDIF
C---- Trace station 2 secondary at station 16 MRCHDU
      IF(TRACE_SIDE.EQ.2 .AND. TRACE_STATION.EQ.16) THEN
       WRITE(*,'(A,9(1X,Z8.8))')
     &  'F_SEC2_16',
     &  TRANSFER(HS2,1),TRANSFER(HS2_T2,1),
     &  TRANSFER(HS2_D2,1),TRANSFER(CF2,1),
     &  TRANSFER(CF2_T2,1),TRANSFER(CF2_D2,1),
     &  TRANSFER(CQ2,1),TRANSFER(CQ2_T2,1),
     &  TRANSFER(CQ2_D2,1)
      ENDIF
      TRACE_PHASE = TRACEPHASE
      IF(((TRACE_SIDE.EQ.1
     &   .AND.(TRACE_STATION.EQ.58.OR.TRACE_STATION.EQ.59))
     &  .OR.(TRACE_SIDE.EQ.2
     &   .AND.(TRACE_STATION.EQ.82.OR.TRACE_STATION.EQ.83)))
     &   .AND. TRACE_ITER.EQ.1) THEN
        WRITE(*,'(A,I1,A,I3,6(1X,Z8.8))')
     &   'F_BLV s',TRACE_SIDE,' i',TRACE_STATION,
     &   TRANSFER(HK2,1), TRANSFER(CF2,1),
     &   TRANSFER(HS2,1), TRANSFER(DI2,1),
     &   TRANSFER(US2,1), TRANSFER(CQ2,1)
        WRITE(*,'(A,I1,A,I3,A,6(1X,Z8.8))')
     &   'F_BLV s',TRACE_SIDE,' i',TRACE_STATION,'_1',
     &   TRANSFER(HK1,1), TRANSFER(CF1,1),
     &   TRANSFER(HS1,1), TRANSFER(DI1,1),
     &   TRANSFER(US1,1), TRANSFER(CQ1,1)
      ENDIF
C
C---- for the similarity station, "1" and "2" variables are the same
      IF(SIMI) THEN
       DO 3 ICOM=1, NCOM
         COM1(ICOM) = COM2(ICOM)
    3  CONTINUE
      ENDIF
C
      CALL TRACE_BLSYS_INTERVAL_INPUTS('BLSYS',
     &     TRACE_SIDE, TRACE_STATION, TRACEPHASE, ITYPB,
     &     WAKE, TURB, TRAN, SIMI,
     &     X1, X2, U1, U2, T1, T2,
     &     D1, D2, S1, S2, DW1, DW2,
     &     AMPL1, AMPL2, M1, M2)
C---- NACA 0018 dump COM1+COM2 at stn 90 s=2 (MRCHUE wake debug)
      IF(TRACE_SIDE.EQ.2 .AND. TRACE_STATION.EQ.90) THEN
       WRITE(0,'(A,6(A,Z8))')
     &  'F_COM1_S90',
     &  ' HK1=',TRANSFER(HK1,1),
     &  ' HS1=',TRANSFER(HS1,1),
     &  ' RT1=',TRANSFER(RT1,1),
     &  ' HK2=',TRANSFER(HK2,1),
     &  ' H2=',TRANSFER(H2,1),
     &  ' M2=',TRANSFER(M2,1)
      ENDIF
C---- NACA 0012 2M a=3: dump COM1 secondary at stn 66 s=2 call 2
      IF(TRACE_SIDE.EQ.2 .AND. TRACE_STATION.EQ.66
     &   .AND. TRACE_PHASE.EQ.1) THEN
       WRITE(0,'(A,7(A,Z8))')
     &  'F_COM1_S66',
     &  ' HK1=',TRANSFER(HK1,1),
     &  ' HS1=',TRANSFER(HS1,1),
     &  ' CF1=',TRANSFER(CF1,1),
     &  ' DI1=',TRANSFER(DI1,1),
     &  ' CQ1=',TRANSFER(CQ1,1),
     &  ' US1=',TRANSFER(US1,1),
     &  ' RT1=',TRANSFER(RT1,1)
      ENDIF
C
C---- set up appropriate finite difference system for current interval
      IF(TRAN) THEN
       CALL TRDIF
      ELSE IF(SIMI) THEN
       CALL BLDIF(0)
      ELSE IF(.NOT.TURB) THEN
       CALL BLDIF(1)
      ELSE IF(WAKE) THEN
       CALL BLDIF(3)
      ELSE IF(TURB) THEN
       CALL BLDIF(2)
      ENDIF
C---- Dump TRDIF VS2 row 1 at transition stations
      IF(TRAN .AND. TRACE_PHASE.EQ.1 .AND.
     &   TRACE_SIDE.EQ.2 .AND. TRACE_STATION.EQ.80) THEN
       WRITE(0,'(A,I1,A,I3,A,Z8,A,Z8,A,Z8,A,Z8,A,Z8,A,Z8)')
     &  'F_TRDIF14 s=',TRACE_SIDE,' i=',TRACE_STATION,
     &  ' V10=',TRANSFER(VS2(1,1),1),
     &  ' V11=',TRANSFER(VS2(1,2),1),
     &  ' V12=',TRANSFER(VS2(1,3),1),
     &  ' V13=',TRANSFER(VS2(1,4),1),
     &  ' V14=',TRANSFER(VS2(1,5),1),
     &  ' R0=',TRANSFER(VSREZ(1),1)
      ENDIF
C---- ah79k135: dump BLDIF inputs+results at 2nd wake IS=2 IBL=69 every call
      IF(TRACE_SIDE.EQ.2 .AND. TRACE_STATION.EQ.69) THEN
        WRITE(0,'(A,I3,A,I4,A,I2,A,L1,A,L1,A,L1,A,L1,
     &    12(1X,A,Z8.8))')
     &    'F_VSTE2 iter=',TRACE_ITER,
     &    ' i=',TRACE_STATION,
     &    ' ityp=',ITYPB,
     &    ' TRAN=',TRAN,' TURB=',TURB,' WAKE=',WAKE,' SIMI=',SIMI,
     &    'T1=',TRANSFER(T1,1),'T2=',TRANSFER(T2,1),
     &    'D1=',TRANSFER(D1,1),'D2=',TRANSFER(D2,1),
     &    'U1=',TRANSFER(U1,1),'U2=',TRANSFER(U2,1),
     &    'HK1=',TRANSFER(HK1,1),'HK2=',TRANSFER(HK2,1),
     &    'HS1=',TRANSFER(HS1,1),'HS2=',TRANSFER(HS2,1),
     &    'DI1=',TRANSFER(DI1,1),'DI2=',TRANSFER(DI2,1)
        WRITE(0,'(A,I3,3(1X,A,Z8.8))')
     &    'F_VSTE2R iter=',TRACE_ITER,
     &    'R1=',TRANSFER(VSREZ(1),1),
     &    'R2=',TRANSFER(VSREZ(2),1),
     &    'R3=',TRANSFER(VSREZ(3),1)
      ENDIF
C---- GDB: dump BLDIF inputs at wake station 83 MRCHUE
      IF(TRACE_SIDE.EQ.2 .AND. TRACE_STATION.EQ.83
     &   .AND. TRACE_ITER.EQ.1 .AND. WAKE) THEN
       WRITE(0,'(A,6(1X,Z8.8))')
     &  'F_BLDIF83_IN',
     &  TRANSFER(T1,1),TRANSFER(D1,1),
     &  TRANSFER(T2,1),TRANSFER(D2,1),
     &  TRANSFER(U1,1),TRANSFER(U2,1)
       WRITE(0,'(A,6(1X,Z8.8))')
     &  'F_BLDIF83_SEC',
     &  TRANSFER(HK1,1),TRANSFER(HS1,1),
     &  TRANSFER(HK2,1),TRANSFER(HS2,1),
     &  TRANSFER(DI1,1),TRANSFER(DI2,1)
       WRITE(0,'(A,3(1X,Z8.8))')
     &  'F_BLDIF83_RES',
     &  TRANSFER(VSREZ(1),1),TRANSFER(VSREZ(2),1),
     &  TRANSFER(VSREZ(3),1)
      ENDIF
C---- GDB: dump VS2(1,3) after BLDIF at station 58
      IF(TRACE_SIDE.EQ.1 .AND. TRACE_STATION.EQ.97) THEN
        WRITE(0,'(A,I2,A,Z8)')
     &   'F_POST_BLDIF ityp=',ITYPB,
     &   ' v13=',TRANSFER(VS2(1,3),1)
      ENDIF
C
C---- HEX TRACE: stations 3-4 side 1 AND station 59 side 2 MRCHDU
      IF(((TRACE_SIDE.EQ.1
     &   .AND. (TRACE_STATION.EQ.3
     &          .OR.TRACE_STATION.EQ.4
     &          .OR.TRACE_STATION.EQ.47
     &          .OR.TRACE_STATION.EQ.35))
     &  .OR. (TRACE_SIDE.EQ.2
     &   .AND. TRACE_STATION.EQ.59))
     &   .AND. TRACEPHASE.EQ.3) THEN
        WRITE(0,'(A,I2,A,I2,A,Z8,A,Z8,'//
     &   'A,Z8,A,Z8,A,Z8)')
     &   'F_STN',TRACE_STATION,
     &   '_CARRY i=',TRACE_ITER,
     &   ' hk1=',TRANSFER(HK1,1),
     &   ' rt1=',TRANSFER(RT1,1),
     &   ' hs1=',TRANSFER(HS1,1),
     &   ' cf1=',TRANSFER(CF1,1),
     &   ' di1=',TRANSFER(DI1,1)
        WRITE(0,'(A,I2,A,I2,A,Z8,A,Z8,'//
     &   'A,Z8,A,Z8,A,Z8)')
     &   'F_STN',TRACE_STATION,
     &   '_CUR   iter=',TRACE_ITER,
     &   ' hk2=',TRANSFER(HK2,1),
     &   ' rt2=',TRANSFER(RT2,1),
     &   ' hs2=',TRANSFER(HS2,1),
     &   ' cf2=',TRANSFER(CF2,1),
     &   ' di2=',TRANSFER(DI2,1)
        WRITE(0,'(A,I2,A,I2,A,Z8,A,Z8,'//
     &   'A,Z8)')
     &   'F_STN',TRACE_STATION,
     &   '_VSREZ iter=',TRACE_ITER,
     &   ' rez1=',TRANSFER(VSREZ(1),1),
     &   ' rez2=',TRANSFER(VSREZ(2),1),
     &   ' rez3=',TRANSFER(VSREZ(3),1)
        WRITE(0,'(A,I2,A,I2,A,Z8,A,Z8,'//
     &   'A,Z8,A,Z8)')
     &   'F_STN',TRACE_STATION,
     &   '_VS2C2 iter=',TRACE_ITER,
     &   ' v12=',TRANSFER(VS2(1,2),1),
     &   ' v13=',TRANSFER(VS2(1,3),1),
     &   ' v22=',TRANSFER(VS2(2,2),1),
     &   ' v23=',TRANSFER(VS2(2,3),1)
        WRITE(0,'(A,I2,A,I2,A,Z8,A,Z8,'//
     &   'A,Z8,A,Z8,A,Z8,A,Z8,A,Z8,A,Z8)')
     &   'F_STN',TRACE_STATION,
     &   '_XU    iter=',TRACE_ITER,
     &   ' x1=',TRANSFER(X1,1),
     &   ' x2=',TRANSFER(X2,1),
     &   ' u1=',TRANSFER(U1,1),
     &   ' u2=',TRANSFER(U2,1),
     &   ' t1=',TRANSFER(T1,1),
     &   ' t2=',TRANSFER(T2,1),
     &   ' d1=',TRANSFER(D1,1),
     &   ' d2=',TRANSFER(D2,1)
        WRITE(0,'(A,I2,A,I2,A,Z8,A,Z8,'//
     &   'A,Z8,A,Z8,A,Z8,A,Z8)')
     &   'F_STN',TRACE_STATION,
     &   '_SEC   iter=',TRACE_ITER,
     &   ' de1=',TRANSFER(DE1,1),
     &   ' de2=',TRANSFER(DE2,1),
     &   ' us1=',TRANSFER(US1,1),
     &   ' us2=',TRANSFER(US2,1),
     &   ' cq1=',TRANSFER(CQ1,1),
     &   ' cq2=',TRANSFER(CQ2,1)
      ENDIF
C
      IF(SIMI) THEN
C----- capture the stored row values before SIMI combines them.
C-     This isolates whether the first remaining row33 mismatch comes
C-     from the BLDIF writeback itself or from the VS1+VS2 sum here.
       CALL TRACE_SIMI_PRECOMBINE_ROWS('BLSYS',
     &      TRACE_SIDE, TRACE_STATION,
     &      VS1(2,2), VS2(2,2), VS1(2,2)+VS2(2,2),
     &      VS1(2,4), VS2(2,4), VS1(2,4)+VS2(2,4),
     &      VS1(3,2), VS2(3,2), VS1(3,2)+VS2(3,2),
     &      VS1(3,3), VS2(3,3), VS1(3,3)+VS2(3,3))
C----- at similarity station, "1" variables are really "2" variables
       DO 10 K=1, 4
         DO 101 L=1, 5
           VS2(K,L) = VS1(K,L) + VS2(K,L)
           VS1(K,L) = 0.
  101    CONTINUE
   10  CONTINUE
      ENDIF
C
C---- change system over into incompressible Uei and Mach
      DO 20 K=1, 4
C
C------ residual derivatives wrt compressible Uec
        RES_U1 = VS1(K,4)
        RES_U2 = VS2(K,4)
        RES_MS = VSM(K)
C
C------ combine with derivatives of compressible  U1,U2 = Uec(Uei M)
        VS1(K,4) = RES_U1*U1_UEI
        VS2(K,4) =                RES_U2*U2_UEI
        VSM(K)   = RES_U1*U1_MS + RES_U2*U2_MS  + RES_MS
   20 CONTINUE
C
C---- GDB: track VS1(2,1) stale value at stations 57-58
      IF((TRACE_STATION.EQ.57.OR.TRACE_STATION.EQ.58)
     &   .AND.(TRACE_SIDE.EQ.1.OR.TRACE_SIDE.EQ.2)) THEN
        WRITE(0,'(A,I1,A,I2,A,I2,A,Z8)')
     &   'F_VS121 s=',TRACE_SIDE,' i=',TRACE_STATION,
     &   ' p=',TRACEPHASE,
     &   ' v=',TRANSFER(VS1(2,1),1)
      ENDIF
      CALL TRACE_EXIT('BLSYS')
      RETURN
      END
 

      SUBROUTINE TESYS(CTE,TTE,DTE)
C--------------------------------------------------------
C     Sets up "dummy" BL system between airfoil TE point 
C     and first wake point infinitesimally behind TE.
C--------------------------------------------------------
      IMPLICIT REAL (M)
      INCLUDE 'XBL.INC'
C
      CALL TRACE_ENTER('TESYS')
C
      DO 55 K=1, 4
        VSREZ(K) = 0.
        VSM(K)   = 0.
        VSR(K)   = 0.
        VSX(K)   = 0.
        DO 551 L=1, 5
          VS1(K,L) = 0.
          VS2(K,L) = 0.
  551   CONTINUE
   55 CONTINUE
C
      CALL BLVAR(3)
      WRITE(0,'(A,Z8,A,Z8,A,Z8,A,Z8)')
     & 'F_TESYS_BLV S2=',TRANSFER(S2,1),
     & ' DI=',TRANSFER(DI2,1),
     & ' CTE=',TRANSFER(CTE,1),
     & ' CF=',TRANSFER(CF2,1)
C
      VS1(1,1) = -1.0
      VS2(1,1) = 1.0
      VSREZ(1) = CTE - S2
C
      VS1(2,2) = -1.0
      VS2(2,2) = 1.0
      VSREZ(2) = TTE - T2
C
      VS1(3,3) = -1.0
      VS2(3,3) = 1.0
      VSREZ(3) = DTE - D2 - DW2
C
      CALL TRACE_EXIT('TESYS')
      RETURN
      END


      SUBROUTINE BLPRV(XSI,AMI,CTI,THI,DSI,DSWAKI,UEI)
C----------------------------------------------------------
C     Set BL primary "2" variables from parameter list
C----------------------------------------------------------
      IMPLICIT REAL(M)
      INCLUDE 'XBL.INC'
C
      CALL TRACE_ENTER('BLPRV')
C
      X2 = XSI
      AMPL2 = AMI
      S2  = CTI
      T2  = THI
      D2  = DSI - DSWAKI
      DW2 = DSWAKI
      IF(TRACE_SIDE.EQ.2 .AND. TRACE_STATION.EQ.90) THEN
       WRITE(0,'(A,A,Z8,A,Z8,A,Z8,A,Z8)')
     &  'F_D2FS_90',
     &  ' d2=',TRANSFER(DSI,1),
     &  ' dw2=',TRANSFER(DSWAKI,1),
     &  ' d2FS=',TRANSFER(D2,1),
     &  ' t2=',TRANSFER(T2,1)
      ENDIF
C
      U2 = UEI*(1.0-TKBL) / (1.0 - TKBL*(UEI/QINFBL)**2)
      U2_UEI = (1.0 + TKBL*(2.0*U2*UEI/QINFBL**2 - 1.0))
     &       / (1.0 - TKBL*(UEI/QINFBL)**2)
      U2_MS  = (U2*(UEI/QINFBL)**2  -  UEI)*TKBL_MS
     &                    / (1.0 - TKBL*(UEI/QINFBL)**2)
C
      CALL TRACE_COMPRESSIBLE_VELOCITY('BLPRV',
     &     UEI, TKBL, QINFBL, TKBL_MS,
     &     U2, U2_UEI, U2_MS)
C
      CALL TRACE_EXIT('BLPRV')
      RETURN
      END ! BLPRV

 
      SUBROUTINE BLKIN
C----------------------------------------------------------
C     Calculates turbulence-independent secondary "2" 
C     variables from the primary "2" variables.
C----------------------------------------------------------
      IMPLICIT REAL(M)
      INCLUDE 'XBL.INC'
C
      CALL TRACE_ENTER('BLKIN')
      CALL TRACE_BLKIN_INPUTS('BLKIN',
     &     U2, T2, D2, DW2,
     &     HSTINV, HSTINV_MS,
     &     GM1BL, RSTBL, RSTBL_MS,
     &     HVRAT, REYBL, REYBL_RE, REYBL_MS)
C
C---- set edge Mach number ** 2
      M2    = U2*U2*HSTINV / (GM1BL*(1.0 - 0.5*U2*U2*HSTINV))
      TR2   = 1.0 + 0.5*GM1BL*M2
      M2_U2 = 2.0*M2*TR2/U2
      M2_MS = U2*U2*TR2    / (GM1BL*(1.0 - 0.5*U2*U2*HSTINV))
     &      * HSTINV_MS
      CALL TRACE_BLKIN_TERMS('BLKIN', U2*U2*HSTINV,
     &     GM1BL*(1.0 - 0.5*U2*U2*HSTINV), TR2, U2*U2*TR2, M2_MS)
C
C---- set edge static density (isentropic relation)
      R2    = RSTBL   *TR2**(-1.0/GM1BL)
      R2_U2 = -R2/TR2 * 0.5*M2_U2
      R2_MS = -R2/TR2 * 0.5*M2_MS
     &      + RSTBL_MS*TR2**(-1.0/GM1BL)
C
C---- set shape parameter
      H2    =  D2/T2
      H2_D2 = 1.0/T2
      H2_T2 = -H2/T2
C---- GDB: dump D2/T2/H2 for wake parity
      IF(WAKE) THEN
       WRITE(0,'(A,Z8,A,Z8,A,Z8)')
     &  'F_BLKIN_H2 D2=',TRANSFER(D2,1),
     &  ' T2=',TRANSFER(T2,1),' H2=',TRANSFER(H2,1)
      ENDIF
C
C---- set edge static/stagnation enthalpy
      HERAT = 1.0 - 0.5*U2*U2*HSTINV
      HE_U2 =     -        U2*HSTINV
      HE_MS =     - 0.5*U2*U2*HSTINV_MS
C
C---- set molecular viscosity
      V2 = SQRT((HERAT)**3) * (1.0+HVRAT)/(HERAT+HVRAT)/REYBL
      V2_HE = V2*(1.5/HERAT - 1.0/(HERAT+HVRAT))
C
      V2_U2 =                        V2_HE*HE_U2
      V2_MS = -V2/REYBL * REYBL_MS + V2_HE*HE_MS
      V2_RE = -V2/REYBL * REYBL_RE
C
C---- set kinematic shape parameter
      CALL HKIN( H2, M2, HK2, HK2_H2, HK2_M2 )
C
      HK2_U2 =                HK2_M2*M2_U2
      HK2_T2 = HK2_H2*H2_T2
      HK2_D2 = HK2_H2*H2_D2
      HK2_MS =                HK2_M2*M2_MS
C
C---- set momentum thickness Reynolds number
      RT2    = R2*U2*T2/V2
      RT2_U2 = RT2*(1.0/U2 + R2_U2/R2 - V2_U2/V2)
      RT2_T2 = RT2/T2
      RT2_MS = RT2*(         R2_MS/R2 - V2_MS/V2)
      RT2_RE = RT2*(                  - V2_RE/V2)
      CALL TRACE_BLKIN_RESULT('BLKIN', U2, T2, D2, DW2,
     &     M2, M2_U2, M2_MS,
     &     R2, R2_U2, R2_MS, H2,
     &     HK2, HK2_U2, HK2_T2, HK2_D2, HK2_MS,
     &     RT2, RT2_U2, RT2_T2, RT2_MS, RT2_RE,
     &     HSTINV_MS, RSTBL_MS, REYBL_MS,
     &     HE_MS, V2_HE, -V2/REYBL*REYBL_MS, V2_HE*HE_MS,
     &     V2, V2_MS)
C
      CALL TRACE_EXIT('BLKIN')
      RETURN
      END ! BLKIN


 
      SUBROUTINE BLVAR(ITYP)
C----------------------------------------------------
C     Calculates all secondary "2" variables from
C     the primary "2" variables X2, U2, T2, D2, S2.
C     Also calculates the sensitivities of the
C     secondary variables wrt the primary variables.
C
C      ITYP = 1 :  laminar
C      ITYP = 2 :  turbulent
C      ITYP = 3 :  turbulent wake
C----------------------------------------------------
      IMPLICIT REAL(M)
      INCLUDE 'XBL.INC'
C
      IF(ITYP.EQ.3) HK2 = MAX(HK2,1.00005)
      IF(ITYP.NE.3) HK2 = MAX(HK2,1.05000)
C
C---- density thickness shape parameter     ( H** )
      CALL HCT( HK2, M2, HC2, HC2_HK2, HC2_M2 )
      HC2_U2 = HC2_HK2*HK2_U2 + HC2_M2*M2_U2
      HC2_T2 = HC2_HK2*HK2_T2
      HC2_D2 = HC2_HK2*HK2_D2
      HC2_MS = HC2_HK2*HK2_MS + HC2_M2*M2_MS
C
C---- set KE thickness shape parameter from  H - H*  correlations
      IF(ITYP.EQ.1) THEN
       CALL HSL( HK2, RT2, M2, HS2, HS2_HK2, HS2_RT2, HS2_M2 )
      ELSE
       CALL HST( HK2, RT2, M2, HS2, HS2_HK2, HS2_RT2, HS2_M2 )
      ENDIF
C
      HS2_U2 = HS2_HK2*HK2_U2 + HS2_RT2*RT2_U2 + HS2_M2*M2_U2
      HS2_T2 = HS2_HK2*HK2_T2 + HS2_RT2*RT2_T2
      HS2_D2 = HS2_HK2*HK2_D2
      HS2_MS = HS2_HK2*HK2_MS + HS2_RT2*RT2_MS + HS2_M2*M2_MS
      HS2_RE =                  HS2_RT2*RT2_RE
C
C---- normalized slip velocity  Us
      US2     = 0.5*HS2*( 1.0 - (HK2-1.0)/(GBCON*H2) )
      US2_HS2 = 0.5  *  ( 1.0 - (HK2-1.0)/(GBCON*H2) )
      US2_HK2 = 0.5*HS2*(     -  1.0     /(GBCON*H2) )
      US2_H2  = 0.5*HS2*        (HK2-1.0)/(GBCON*H2**2)
C
      US2_U2 = US2_HS2*HS2_U2 + US2_HK2*HK2_U2
      US2_T2 = US2_HS2*HS2_T2 + US2_HK2*HK2_T2 + US2_H2*H2_T2
      US2_D2 = US2_HS2*HS2_D2 + US2_HK2*HK2_D2 + US2_H2*H2_D2
      US2_MS = US2_HS2*HS2_MS + US2_HK2*HK2_MS
      US2_RE = US2_HS2*HS2_RE
C
      IF(ITYP.LE.2 .AND. US2.GT.0.95) THEN
CCC       WRITE(*,*) 'BLVAR: Us clamped:', US2
       US2 = 0.98
       US2_U2 = 0.
       US2_T2 = 0.
       US2_D2 = 0.
       US2_MS = 0.
       US2_RE = 0.
      ENDIF
C
      IF(ITYP.EQ.3 .AND. US2.GT.0.99995) THEN
CCC       WRITE(*,*) 'BLVAR: Wake Us clamped:', US2
       US2 = 0.99995
       US2_U2 = 0.
       US2_T2 = 0.
       US2_D2 = 0.
       US2_MS = 0.
       US2_RE = 0.
      ENDIF
C
C---- equilibrium wake layer shear coefficient (Ctau)EQ ** 1/2
C   ...  NEW  12 Oct 94
      GCC = 0.0
      HKC = HK2 - 1.0
      HKC_HK2 = 1.0
      HKC_RT2 = 0.0
      IF(ITYP.EQ.2) THEN
       GCC = GCCON
       HKC     = HK2 - 1.0 - GCC/RT2
       HKC_HK2 = 1.0
       HKC_RT2 =             GCC/RT2**2
       IF(HKC .LT. 0.01) THEN
        HKC = 0.01
        HKC_HK2 = 0.0
        HKC_RT2 = 0.0
       ENDIF
      ENDIF
C
      HKB = HK2 - 1.0
      USB = 1.0 - US2
      CQNUM = CTCON*HS2*HKB*HKC**2
      CQDEN = USB*H2*HK2**2
      CQRAT = CQNUM/CQDEN
      CQ2     =
     &    SQRT( CQRAT )
      IF(TRACE_STATION.GE.65 .AND. TRACE_STATION.LE.70
     &   .AND. TRACE_SIDE.EQ.2) THEN
       WRITE(0,'(A,I1,11(A,Z8))') 'F_CQ_TRACE ityp=',ITYP,
     &  ' hk=',TRANSFER(HK2,1),
     &  ' rt=',TRANSFER(RT2,1),
     &  ' hs=',TRANSFER(HS2,1),
     &  ' us=',TRANSFER(US2,1),
     &  ' h=',TRANSFER(H2,1),
     &  ' hkc=',TRANSFER(HKC,1),
     &  ' hkb=',TRANSFER(HKB,1),
     &  ' usb=',TRANSFER(USB,1),
     &  ' num=',TRANSFER(CQNUM,1),
     &  ' den=',TRANSFER(CQDEN,1),
     &  ' arg=',TRANSFER(CQRAT,1)
      ENDIF
      CALL TRACE_CQ_TERMS('BLVAR', ITYP, HK2, HS2, US2, H2, RT2,
     &     HKC, HKB, USB, CQNUM, CQDEN, CQRAT, CQ2)
      CQ2_HS2 = CTCON    *HKB*HKC**2 / (USB*H2*HK2**2)       * 0.5/CQ2
      CQ2_US2 = CTCON*HS2*HKB*HKC**2 / (USB*H2*HK2**2) / USB * 0.5/CQ2
      CQ2_HK2 = CTCON*HS2    *HKC**2 / (USB*H2*HK2**2)       * 0.5/CQ2
     &        - CTCON*HS2*HKB*HKC**2 / (USB*H2*HK2**3) * 2.0 * 0.5/CQ2
     &        + CTCON*HS2*HKB*HKC    / (USB*H2*HK2**2) * 2.0 * 0.5/CQ2
     &         *HKC_HK2
      CQ2_RT2 = CTCON*HS2*HKB*HKC    / (USB*H2*HK2**2) * 2.0 * 0.5/CQ2
     &         *HKC_RT2
      CQ2_H2  =-CTCON*HS2*HKB*HKC**2 / (USB*H2*HK2**2) / H2  * 0.5/CQ2
C
      CQ2_U2 = CQ2_HS2*HS2_U2 + CQ2_US2*US2_U2 + CQ2_HK2*HK2_U2
      CQ2_T2 = CQ2_HS2*HS2_T2 + CQ2_US2*US2_T2 + CQ2_HK2*HK2_T2
      CQ2_D2 = CQ2_HS2*HS2_D2 + CQ2_US2*US2_D2 + CQ2_HK2*HK2_D2
      CQ2_MS = CQ2_HS2*HS2_MS + CQ2_US2*US2_MS + CQ2_HK2*HK2_MS
      CQ2_RE = CQ2_HS2*HS2_RE + CQ2_US2*US2_RE
C
      CQ2_U2 = CQ2_U2                + CQ2_RT2*RT2_U2
      CQ2_T2 = CQ2_T2 + CQ2_H2*H2_T2 + CQ2_RT2*RT2_T2
      CQ2_D2 = CQ2_D2 + CQ2_H2*H2_D2
      CQ2_MS = CQ2_MS                + CQ2_RT2*RT2_MS
      CQ2_RE = CQ2_RE                + CQ2_RT2*RT2_RE
C---- n6h20 derivative trace: secondary derivatives at IBL=66 iter 2 mc=10
      IF(TRACE_SIDE.EQ.2 .AND. TRACE_STATION.EQ.66
     &   .AND. TRACE_ITER.EQ.2 .AND. TRACE_OUTER.EQ.10) THEN
       WRITE(0,'(A,I2,2(A,Z8))') 'F_HSDRV10_66 it=',TRACE_ITER,
     &  ' HST=',TRANSFER(HS2_T2,1), ' HSD=',TRANSFER(HS2_D2,1)
       WRITE(0,'(A,I2,2(A,Z8))') 'F_CFDRV10_66 it=',TRACE_ITER,
     &  ' CFT=',TRANSFER(CF2_T2,1), ' CFD=',TRANSFER(CF2_D2,1)
       WRITE(0,'(A,I2,2(A,Z8))') 'F_DIDRV10_66 it=',TRACE_ITER,
     &  ' DIT=',TRANSFER(DI2_T2,1), ' DID=',TRANSFER(DI2_D2,1)
       WRITE(0,'(A,I2,2(A,Z8))') 'F_USDRV10_66 it=',TRACE_ITER,
     &  ' UST=',TRANSFER(US2_T2,1), ' USD=',TRANSFER(US2_D2,1)
       WRITE(0,'(A,I2,2(A,Z8))') 'F_CQDRV10_66 it=',TRACE_ITER,
     &  ' CQT=',TRANSFER(CQ2_T2,1), ' CQD=',TRANSFER(CQ2_D2,1)
      ENDIF
      CALL TRACE_CQ_DERIVATIVE_TERMS('BLVAR', ITYP,
     & HK2, HS2, US2, H2, RT2,
     & CQ2_HS2, CQ2_US2, CQ2_HK2, CQ2_H2, CQ2_RT2,
     & CTCON*HS2*HKC**2 / (USB*H2*HK2**2),
     & CTCON*HS2*HKB*HKC**2 / (USB*H2*HK2**3) * 2.0,
     & CTCON*HS2*HKB*HKC / (USB*H2*HK2**2) * 2.0,
     & CQ2_HS2*HS2_T2, CQ2_US2*US2_T2, CQ2_HK2*HK2_T2,
     & CQ2_H2*H2_T2, CQ2_RT2*RT2_T2,
     & CQ2_HS2*HS2_D2, CQ2_US2*US2_D2, CQ2_HK2*HK2_D2,
     & CQ2_H2*H2_D2,
     & CQ2_T2, CQ2_D2)
C
C
C---- set skin friction coefficient 
      IF(ITYP.EQ.3) THEN
C----- wake
       CF2     = 0.
       CF2_HK2 = 0.
       CF2_RT2 = 0.
       CF2_M2  = 0.
      ELSE IF(ITYP.EQ.1) THEN
C----- laminar
       CALL CFL( HK2, RT2, M2, CF2, CF2_HK2, CF2_RT2, CF2_M2 )
      ELSE
C----- turbulent
       CALL CFT( HK2, RT2, M2, CF2, CF2_HK2, CF2_RT2, CF2_M2 )
       CALL CFL( HK2, RT2, M2, CF2L,CF2L_HK2,CF2L_RT2,CF2L_M2)
       IF(CF2L.GT.CF2) THEN
C------- laminar Cf is greater than turbulent Cf -- use laminar
C-       (this will only occur for unreasonably small Rtheta)
ccc      write(*,*) 'Cft Cfl Rt Hk:', CF2, CF2L, RT2, HK2, X2
         CF2     = CF2L
         CF2_HK2 = CF2L_HK2
         CF2_RT2 = CF2L_RT2
         CF2_M2  = CF2L_M2
       ENDIF
      ENDIF
C
      CF2_U2 = CF2_HK2*HK2_U2 + CF2_RT2*RT2_U2 + CF2_M2*M2_U2
      CF2_T2 = CF2_HK2*HK2_T2 + CF2_RT2*RT2_T2
      CF2_D2 = CF2_HK2*HK2_D2
      CF2_MS = CF2_HK2*HK2_MS + CF2_RT2*RT2_MS + CF2_M2*M2_MS
      CF2_RE =                  CF2_RT2*RT2_RE
      CALL TRACE_BLVAR_CF_TERMS('BLVAR', ITYP, 2,
     &     HK2_MS, RT2_MS, M2_MS,
     &     CF2, CF2_HK2, CF2_RT2, CF2_M2,
     &     CF2_U2, CF2_T2, CF2_D2, CF2_MS, RT2_RE, CF2_RE)
C
C---- dissipation function    2 CD / H*
      IF(ITYP.EQ.1) THEN
C
C----- laminar
       CALL DIL( HK2, RT2, DI2, DI2_HK2, DI2_RT2 )
C
       DI2_U2 = DI2_HK2*HK2_U2 + DI2_RT2*RT2_U2
       DI2_T2 = DI2_HK2*HK2_T2 + DI2_RT2*RT2_T2
       DI2_D2 = DI2_HK2*HK2_D2
       DI2_S2 = 0.
       DI2_MS = DI2_HK2*HK2_MS + DI2_RT2*RT2_MS
       DI2_RE =                  DI2_RT2*RT2_RE
C
      ELSE IF(ITYP.EQ.2) THEN
C
CCC       CALL DIT(     HS2,     US2,     CF2,     S2, DI2,
CCC     &           DI2_HS2, DI2_US2, DI2_CF2, DI2_S2      )
C
C----- turbulent wall contribution
       CALL CFT(HK2, RT2, M2, CF2T, CF2T_HK2, CF2T_RT2, CF2T_M2)
       CF2T_U2 = CF2T_HK2*HK2_U2 + CF2T_RT2*RT2_U2 + CF2T_M2*M2_U2
       CF2T_T2 = CF2T_HK2*HK2_T2 + CF2T_RT2*RT2_T2
       CF2T_D2 = CF2T_HK2*HK2_D2
       CF2T_MS = CF2T_HK2*HK2_MS + CF2T_RT2*RT2_MS + CF2T_M2*M2_MS
       CF2T_RE =                   CF2T_RT2*RT2_RE
C
       DI2      =  ( 0.5*CF2T*US2 ) * 2.0/HS2
       IF(TRACE_SIDE.EQ.2 .AND. TRACE_STATION.EQ.5) THEN
        WRITE(0,'(A,Z8,A,Z8,A,Z8,A,Z8,A,Z8)')
     &   'F_TURB_DI2 CF2T=',TRANSFER(CF2T,1),
     &   ' US2=',TRANSFER(US2,1),
     &   ' HS2=',TRANSFER(HS2,1),
     &   ' DI2=',TRANSFER(DI2,1),
     &   ' S2=',TRANSFER(S2,1)
       ENDIF
       DI2_HS2  = -( 0.5*CF2T*US2 ) * 2.0/HS2**2
       DI2_US2  =  ( 0.5*CF2T     ) * 2.0/HS2
       DI2_CF2T =  ( 0.5     *US2 ) * 2.0/HS2
       DIWALLRAW = DI2
       DIWALLHS = DI2_HS2
       DIWALLUS = DI2_US2
       DIWALLCF = DI2_CF2T
C
       DI2_S2 = 0.0
       DI2_U2 = DI2_HS2*HS2_U2 + DI2_US2*US2_U2 + DI2_CF2T*CF2T_U2
       DI2_T2 = DI2_HS2*HS2_T2 + DI2_US2*US2_T2 + DI2_CF2T*CF2T_T2
       DI2_D2 = DI2_HS2*HS2_D2 + DI2_US2*US2_D2 + DI2_CF2T*CF2T_D2
       DIWALLDPRE = DI2_D2
       DI2_MS = DI2_HS2*HS2_MS + DI2_US2*US2_MS + DI2_CF2T*CF2T_MS
       DI2_RE = DI2_HS2*HS2_RE + DI2_US2*US2_RE + DI2_CF2T*CF2T_RE
C
C
C----- set minimum Hk for wake layer to still exist
       GRT = LOG(RT2)
       HMIN = 1.0 + 2.1/GRT
       HM_RT2 = -(2.1/GRT**2) / RT2
C
C----- set factor DFAC for correcting wall dissipation for very low Hk
       FL = (HK2-1.0)/(HMIN-1.0)
       FL_HK2 =   1.0/(HMIN-1.0)
       FL_RT2 = ( -FL/(HMIN-1.0) ) * HM_RT2
C
       TFL = TANH(FL)
       DFAC  = 0.5 + 0.5* TFL
       DF_FL =       0.5*(1.0 - TFL**2)
C
       DF_HK2 = DF_FL*FL_HK2
       DF_RT2 = DF_FL*FL_RT2
       DFTERMD = DF_HK2*HK2_D2
C
       DI2_S2 = DI2_S2*DFAC
       DI2_U2 = DI2_U2*DFAC + DI2*(DF_HK2*HK2_U2 + DF_RT2*RT2_U2)
       DI2_T2 = DI2_T2*DFAC + DI2*(DF_HK2*HK2_T2 + DF_RT2*RT2_T2)
       DI2_D2 = DI2_D2*DFAC + DI2*(DF_HK2*HK2_D2                )
       DIWALLDPOST = DI2_D2
       DI2_MS = DI2_MS*DFAC + DI2*(DF_HK2*HK2_MS + DF_RT2*RT2_MS)
       DI2_RE = DI2_RE*DFAC + DI2*(                DF_RT2*RT2_RE)
       DI2    = DI2   *DFAC
C
      ELSE
C
C----- zero wall contribution for wake
       DI2    = 0.0
       DI2_S2 = 0.0
       DI2_U2 = 0.0
       DI2_T2 = 0.0
       DI2_D2 = 0.0
       DI2_MS = 0.0
       DI2_RE = 0.0
C
      ENDIF
C
C
C---- Add on turbulent outer layer contribution
      IF(ITYP.NE.1) THEN
C
       DD     =  S2**2 * (0.995-US2) * 2.0/HS2
       DD_HS2 = -S2**2 * (0.995-US2) * 2.0/HS2**2
       DD_US2 = -S2**2               * 2.0/HS2
       DD_S2  =  S2*2.0* (0.995-US2) * 2.0/HS2
       DDOUT = DD
       IF(ITYP.EQ.3 .AND. TRACE_STATION.EQ.71 .AND. TRACE_SIDE.EQ.2
     &    .AND. MRCHDU_COUNT.EQ.10) THEN
         WRITE(0,'(A,8(A,Z8))') 'F_BLVAR_DI_MC10',
     &    ' hk=',TRANSFER(HK2,1),
     &    ' rt=',TRANSFER(RT2,1),
     &    ' hs=',TRANSFER(HS2,1),
     &    ' us=',TRANSFER(US2,1),
     &    ' S2=',TRANSFER(S2,1),
     &    ' ddO=',TRANSFER(DD,1)
       ENDIF
       DDOUTHS = DD_HS2
       DDOUTUS = DD_US2
       DDOUTD = DD_HS2*HS2_D2 + DD_US2*US2_D2
C
       DI2    = DI2    + DD
       DI2_S2 =          DD_S2
       DI2_U2 = DI2_U2 + DD_HS2*HS2_U2 + DD_US2*US2_U2
       DI2_T2 = DI2_T2 + DD_HS2*HS2_T2 + DD_US2*US2_T2
       DI2_D2 = DI2_D2 + DD_HS2*HS2_D2 + DD_US2*US2_D2
       DI2_MS = DI2_MS + DD_HS2*HS2_MS + DD_US2*US2_MS
       DI2_RE = DI2_RE + DD_HS2*HS2_RE + DD_US2*US2_RE
C
C----- add laminar stress contribution to outer layer CD
c###
       DD     =  0.15*(0.995-US2)**2 / RT2  * 2.0/HS2
       DD_US2 = -0.15*(0.995-US2)*2. / RT2  * 2.0/HS2
       DD_HS2 = -DD/HS2
       DD_RT2 = -DD/RT2
       DDLLAM = DD
       DDLHS = DD_HS2
       DDLUS = DD_US2
       DDLRT = DD_RT2
       DDLD = DD_HS2*HS2_D2 + DD_US2*US2_D2
C
       DI2    = DI2    + DD
       DI2_U2 = DI2_U2 + DD_HS2*HS2_U2 + DD_US2*US2_U2 + DD_RT2*RT2_U2
       DI2_T2 = DI2_T2 + DD_HS2*HS2_T2 + DD_US2*US2_T2 + DD_RT2*RT2_T2
       DI2_D2 = DI2_D2 + DD_HS2*HS2_D2 + DD_US2*US2_D2
       DI2_MS = DI2_MS + DD_HS2*HS2_MS + DD_US2*US2_MS + DD_RT2*RT2_MS
       DI2_RE = DI2_RE + DD_HS2*HS2_RE + DD_US2*US2_RE + DD_RT2*RT2_RE
       CALL TRACE_BLVAR_OUTER_DI_TERMS('BLVAR', 2, ITYP,
     &      HS2_T2, US2_T2, RT2_T2,
     &      DDOUT, DDOUTHS, DDOUTUS, DD_S2,
     &      DDOUTHS*HS2_T2 + DDOUTUS*US2_T2,
     &      DDLLAM, DDLHS, DDLUS, DDLRT,
     &      DDLHS*HS2_T2 + DDLUS*US2_T2 + DDLRT*RT2_T2,
     &      DI2_T2)
C
      ENDIF
C
C
      IF(ITYP.EQ.2) THEN
        DILUSED = 0.0
        CALL DIL( HK2, RT2, DI2L, DI2L_HK2, DI2L_RT2 )
C
        IF(DI2L.GT.DI2) THEN
C------- laminar CD is greater than turbulent CD -- use laminar
C-       (this will only occur for unreasonably small Rtheta)
ccc       write(*,*) 'CDt CDl Rt Hk:', DI2, DI2L, RT2, HK2
          DILUSED = 1.0
          DI2    = DI2L
          DI2_S2 = 0.
          DI2_U2 = DI2L_HK2*HK2_U2 + DI2L_RT2*RT2_U2
          DI2_T2 = DI2L_HK2*HK2_T2 + DI2L_RT2*RT2_T2
          DI2_D2 = DI2L_HK2*HK2_D2
          DI2_MS = DI2L_HK2*HK2_MS + DI2L_RT2*RT2_MS
          DI2_RE =                   DI2L_RT2*RT2_RE
        ENDIF
        CALL TRACE_BLVAR_TURBULENT_D_UPDATE('BLVAR',
     &       S2, DIWALLDPOST,
     &       DDOUTHS, HS2_HK2, HK2_D2, HS2_D2, DDOUTUS, US2_D2, DDOUTD,
     &       DDLHS, DDLUS, DDLD, DI2_D2)
        CALL TRACE_BLVAR_TURBULENT_DI_TERMS('BLVAR',
     &       S2, HK2, HS2, US2, RT2,
     &       CF2T, CF2T_HK2, CF2T_RT2, CF2T_M2, CF2T_D2,
     &       DIWALLRAW, DIWALLHS, DIWALLUS, DIWALLCF, DIWALLDPRE,
     &       GRT, HMIN, HM_RT2, FL, DFAC, DF_HK2, DF_RT2, DFTERMD,
     &       DIWALLDPOST, DDOUT, DDOUTHS, DDOUTUS, DDOUTD,
     &       DDLLAM, DDLHS, DDLUS, DDLRT, DDLD,
     &       DI2L, DI2L_HK2, DI2L_RT2, DILUSED, DI2, DI2_D2)
      ENDIF
C
cC----- add on CD contribution of inner shear layer
c       IF(ITYP.EQ.3 .AND. DW2.GT.0.0) THEN
c        DKON = 0.03*0.75**3
c        DDI = DKON*US2**3
c        DDI_US2 = 3.0*DKON*US2**2
c        DI2 = DI2 + DDI * DW2/DWTE
c        DI2_U2 = DI2_U2 + DDI_US2*US2_U2 * DW2/DWTE
c        DI2_T2 = DI2_T2 + DDI_US2*US2_T2 * DW2/DWTE
c        DI2_D2 = DI2_D2 + DDI_US2*US2_D2 * DW2/DWTE
c        DI2_MS = DI2_MS + DDI_US2*US2_MS * DW2/DWTE
c        DI2_RE = DI2_RE + DDI_US2*US2_RE * DW2/DWTE
c       ENDIF
C
      IF(ITYP.EQ.3) THEN
C------ laminar wake CD
        CALL DILW( HK2, RT2, DI2L, DI2L_HK2, DI2L_RT2 )
        IF(TRACE_STATION.EQ.93 .AND. TRACE_SIDE.EQ.2
     &     .AND. HK2 .LT. 1.001) THEN
         WRITE(0,'(A,3(A,Z8),A,L1,4(A,Z8))')
     &    'F_DILW93',
     &    ' dilw=',TRANSFER(DI2L,1),
     &    ' ddi=',TRANSFER(DI2,1),
     &    ' diTt=',TRANSFER(DI2_T2,1),
     &    ' gt=',DI2L .GT. DI2,
     &    ' dHk=',TRANSFER(DI2L_HK2,1),
     &    ' hkT=',TRANSFER(HK2_T2,1),
     &    ' dRt=',TRANSFER(DI2L_RT2,1),
     &    ' rtT=',TRANSFER(RT2_T2,1)
        ENDIF
        IF(DI2L .GT. DI2) THEN
C------- laminar wake CD is greater than turbulent CD -- use laminar
C-       (this will only occur for unreasonably small Rtheta)
ccc         write(*,*) 'CDt CDl Rt Hk:', DI2, DI2L, RT2, HK2
         DI2    = DI2L
         DI2_S2 = 0.
         DI2_U2 = DI2L_HK2*HK2_U2 + DI2L_RT2*RT2_U2
         DI2_T2 = DI2L_HK2*HK2_T2 + DI2L_RT2*RT2_T2
         DI2_D2 = DI2L_HK2*HK2_D2
         DI2_MS = DI2L_HK2*HK2_MS + DI2L_RT2*RT2_MS
         DI2_RE =                   DI2L_RT2*RT2_RE
        ENDIF
      ENDIF
C
C
      IF(ITYP.EQ.3) THEN
C----- double dissipation for the wake (two wake halves)
       DI2    = DI2   *2.0
       DI2_S2 = DI2_S2*2.0
       DI2_U2 = DI2_U2*2.0
       DI2_T2 = DI2_T2*2.0
       DI2_D2 = DI2_D2*2.0
       DI2_MS = DI2_MS*2.0
       DI2_RE = DI2_RE*2.0
      ENDIF
C
      IF(ITYP.EQ.1) THEN
       CALL TRACE_BLVAR_LAMINAR_DI_TERMS('BLVAR', 2,
     &      HK2, RT2, DI2, DI2_HK2, DI2_RT2, HK2_T2, RT2_T2, DI2_T2)
      ENDIF
C
C---- BL thickness (Delta) from simplified Green's correlation
      DE2     = (3.15 + 1.72/(HK2-1.0)   )*T2  +  D2
      DE2_HK2 = (     - 1.72/(HK2-1.0)**2)*T2
C
      DE2_U2 = DE2_HK2*HK2_U2
      DE2_T2 = DE2_HK2*HK2_T2 + (3.15 + 1.72/(HK2-1.0))
      DE2_D2 = DE2_HK2*HK2_D2 + 1.0
      DE2_MS = DE2_HK2*HK2_MS
C
ccc      HDMAX = 15.0
      HDMAX = 12.0
      IF(DE2 .GT. HDMAX*T2) THEN
cccc      IF(DE2 .GT. HDMAX*T2 .AND. (HK2 .GT. 4.0 .OR. ITYP.EQ.3)) THEN
       DE2    = HDMAX*T2
       DE2_U2 =  0.0
       DE2_T2 = HDMAX
       DE2_D2 =  0.0
       DE2_MS =  0.0
      ENDIF
C
      RETURN
      END
 

      SUBROUTINE BLMID(ITYP)
C----------------------------------------------------
C     Calculates midpoint skin friction CFM
C
C      ITYP = 1 :  laminar
C      ITYP = 2 :  turbulent
C      ITYP = 3 :  turbulent wake
C----------------------------------------------------
      IMPLICIT REAL(M)
      INCLUDE 'XBL.INC'
C
C---- set similarity variables if not defined
      IF(SIMI) THEN
       HK1    = HK2
       HK1_T1 = HK2_T2
       HK1_D1 = HK2_D2
       HK1_U1 = HK2_U2
       HK1_MS = HK2_MS
       RT1    = RT2
       RT1_T1 = RT2_T2
       RT1_U1 = RT2_U2
       RT1_MS = RT2_MS
       RT1_RE = RT2_RE
       M1    = M2
       M1_U1 = M2_U2
       M1_MS = M2_MS
      ENDIF
C
C---- define stuff for midpoint CF
      HKA = 0.5*(HK1 + HK2)
      RTA = 0.5*(RT1 + RT2)
      MA  = 0.5*(M1  + M2 )
C
C---- midpoint skin friction coefficient  (zero in wake)
      CFMTURB = 0.
      CFMTURB_HKA = 0.
      CFMTURB_RTA = 0.
      CFMTURB_MA = 0.
      CFML = 0.
      CFML_HKA = 0.
      CFML_RTA = 0.
      CFML_MA = 0.
      IUSELAM = 0
      IF(ITYP.EQ.3) THEN
       CFM     = 0.
       CFM_HKA = 0.
       CFM_RTA = 0.
       CFM_MA  = 0.
       CFM_MS  = 0.
      ELSE IF(ITYP.EQ.1) THEN
       CALL CFL( HKA, RTA, MA, CFM, CFM_HKA, CFM_RTA, CFM_MA )
       CFML = CFM
       CFML_HKA = CFM_HKA
       CFML_RTA = CFM_RTA
       CFML_MA = CFM_MA
       IUSELAM = 1
      ELSE
       CALL CFT( HKA, RTA, MA, CFM, CFM_HKA, CFM_RTA, CFM_MA )
       CFMTURB = CFM
       CFMTURB_HKA = CFM_HKA
       CFMTURB_RTA = CFM_RTA
       CFMTURB_MA = CFM_MA
       CALL CFL( HKA, RTA, MA, CFML,CFML_HKA,CFML_RTA,CFML_MA)
       IF(CFML.GT.CFM) THEN
ccc      write(*,*) 'Cft Cfl Rt Hk:', CFM, CFML, RTA, HKA, 0.5*(X1+X2)
         CFM     = CFML
         CFM_HKA = CFML_HKA
         CFM_RTA = CFML_RTA
         CFM_MA  = CFML_MA
         IUSELAM = 1
       ENDIF
      ENDIF
      CALL TRACE_BLMID_CANDIDATE_CF_TERMS('BLMID', ITYP,
     &     HKA, RTA, MA,
     &     CFMTURB, CFMTURB_HKA, CFMTURB_RTA, CFMTURB_MA,
     &     CFML, CFML_HKA, CFML_RTA, CFML_MA,
     &     IUSELAM, CFM, CFM_HKA, CFM_RTA, CFM_MA)
C
      CFM_U1 = 0.5*(CFM_HKA*HK1_U1 + CFM_MA*M1_U1 + CFM_RTA*RT1_U1)
      CFM_T1 = 0.5*(CFM_HKA*HK1_T1 +                CFM_RTA*RT1_T1)
      CFM_D1 = 0.5*(CFM_HKA*HK1_D1                                )
C
      CFM_U2 = 0.5*(CFM_HKA*HK2_U2 + CFM_MA*M2_U2 + CFM_RTA*RT2_U2)
      CFM_T2 = 0.5*(CFM_HKA*HK2_T2 +                CFM_RTA*RT2_T2)
      CFM_D2 = 0.5*(CFM_HKA*HK2_D2                                )
C
      CFM_MS = 0.5*(CFM_HKA*HK1_MS + CFM_MA*M1_MS + CFM_RTA*RT1_MS
     &            + CFM_HKA*HK2_MS + CFM_MA*M2_MS + CFM_RTA*RT2_MS)
      CFM_RE = 0.5*(                                CFM_RTA*RT1_RE
     &                                            + CFM_RTA*RT2_RE)
      CALL TRACE_BLMID_CF_TERMS('BLMID', ITYP,
     &     HK1_MS, RT1_MS, M1_MS,
     &     HK2_MS, RT2_MS, M2_MS,
     &     CFM, CFM_HKA, CFM_RTA, CFM_MA,
     &     HK1_T1, RT1_T1, HK2_T2, RT2_T2,
     &     CFM_U1, CFM_T1, CFM_D1,
     &     CFM_U2, CFM_T2, CFM_D2,
     &     CFM_MS, RT1_RE, RT2_RE, CFM_RE)
C
      RETURN
      END ! BLMID

 
      SUBROUTINE TRDIF
C-----------------------------------------------
C     Sets up the Newton system governing the
C     transition interval.  Equations governing
C     the  laminar  part  X1 < xi < XT  and
C     the turbulent part  XT < xi < X2
C     are simply summed.
C-----------------------------------------------
      IMPLICIT REAL(M)
      INCLUDE 'XBL.INC'
      REAL  BL1(4,5), BL2(4,5), BLREZ(4), BLM(4), BLR(4), BLX(4)
     &    , BT1(4,5), BT2(4,5), BTREZ(4), BTM(4), BTR(4), BTX(4)
      REAL  ROW13BASETERM, ROW13UPWTERM, ROW13DETERM, ROW13USTERM
     &    , ROW13TRANSPORT, ROW13CQTERM, ROW13CFTERM, ROW13HKTERM
     &    , ROW23BASETERM, ROW23UPWTERM, ROW23DETERM, ROW23USTERM
     &    , ROW23TRANSPORT, ROW23CQTERM, ROW23CFTERM, ROW23HKTERM
      REAL  X1ORIG, T1ORIG, D1ORIG, U1ORIG
      REAL  BL1VS121, BL1P1, BL1P2, BL1P3, BL1P4
C
C---- save variables and sensitivities for future restoration
      WRITE(0,'(A,I2,A,I3)') 'F_TRDIF_ENTRY IS=',
     & TRACE_SIDE,' IBL=',TRACE_STATION
      IF(TRACE_SIDE.EQ.2 .AND. TRACE_STATION.EQ.66
     &   .AND. TRACE_ITER.EQ.2 .AND. TRACE_OUTER.EQ.10) THEN
       WRITE(0,'(A,A,Z8)') 'F_TRPT10_66_XT',
     &  ' XT=',TRANSFER(XT,1)
      ENDIF
      X1ORIG = X1
      T1ORIG = T1
      D1ORIG = D1
      U1ORIG = U1
      DO 5 ICOM=1, NCOM
        C1SAV(ICOM) = COM1(ICOM)
        C2SAV(ICOM) = COM2(ICOM)
    5 CONTINUE
C
C---- weighting factors for linear interpolation to transition point
      WF2    = (XT-X1)/(X2-X1)
      WF2_XT = 1.0/(X2-X1)
      IF(TRACE_SIDE.EQ.2 .AND.
     &   (TRACE_STATION.EQ.50.OR.TRACE_STATION.EQ.80)) THEN
       WRITE(0,'(A,I3,A,Z8,A,Z8,A,Z8,A,Z8)')
     &  'F_TRDIF_XF i=',TRACE_STATION,
     &  ' XT=',TRANSFER(XT,1),
     &  ' WF2=',TRANSFER(WF2,1),
     &  ' X1=',TRANSFER(X1,1),
     &  ' X2=',TRANSFER(X2,1)
      ENDIF
C---- TRDIF parity trace at side 1 station 78 (NACA 0009 case 188)
      IF(TRACE_SIDE.EQ.1 .AND. TRACE_STATION.EQ.78) THEN
       WRITE(0,'(A,I3,A,Z8,A,Z8,A,Z8,A,Z8)')
     &  'F_TRDIF_S1I78 i=',TRACE_STATION,
     &  ' XT=',TRANSFER(XT,1),
     &  ' WF2=',TRANSFER(WF2,1),
     &  ' X1=',TRANSFER(X1,1),
     &  ' X2=',TRANSFER(X2,1)
      ENDIF
C
      WF2_A1 = WF2_XT*XT_A1
      WF2_X1 = WF2_XT*XT_X1 + (WF2-1.0)/(X2-X1)
      WF2_X2 = WF2_XT*XT_X2 -  WF2     /(X2-X1)
      WF2_T1 = WF2_XT*XT_T1
      WF2_T2 = WF2_XT*XT_T2
      WF2_D1 = WF2_XT*XT_D1
      WF2_D2 = WF2_XT*XT_D2
      WF2_U1 = WF2_XT*XT_U1
      WF2_U2 = WF2_XT*XT_U2
      WF2_MS = WF2_XT*XT_MS
      WF2_RE = WF2_XT*XT_RE
      WF2_XF = WF2_XT*XT_XF
      IF(TRACE_SIDE.EQ.2 .AND. TRACE_STATION.EQ.80) THEN
       WRITE(0,'(A,A,Z8,A,Z8)')
     &  'F_WF2XF80',
     &  ' wf2xf=',TRANSFER(WF2_XF,1),
     &  ' xtxf=',TRANSFER(XT_XF,1)
      ENDIF
      IF(TRACE_SIDE.EQ.1 .AND. TRACE_STATION.EQ.78) THEN
       WRITE(0,'(A,A,Z8,A,Z8,A,Z8,A,Z8)')
     &  'F_WF2XF78',
     &  ' wf2xf=',TRANSFER(WF2_XF,1),
     &  ' xtxf=',TRANSFER(XT_XF,1),
     &  ' wf2_xt=',TRANSFER(WF2_XT,1),
     &  ' wf2=',TRANSFER(WF2,1)
      ENDIF
      IF(TRACE_SIDE.EQ.2 .AND. TRACE_STATION.EQ.5
     &   .AND. TRACE_ITER.EQ.1) THEN
        WRITE(0,'(A,12(1X,Z8.8))') 'G_WF',
     &    TRANSFER(XT,1), TRANSFER(X1,1), TRANSFER(X2,1),
     &    TRANSFER(WF2,1), TRANSFER(WF2_XT,1),
     &    TRANSFER(WF2_A1,1), TRANSFER(WF2_T1,1),
     &    TRANSFER(WF2_T2,1), TRANSFER(WF2_D1,1),
     &    TRANSFER(WF2_D2,1), TRANSFER(WF2_U1,1),
     &    TRANSFER(WF2_U2,1)
        WRITE(0,'(A,9(1X,Z8.8))') 'G_XT',
     &    TRANSFER(XT_A1,1), TRANSFER(XT_T1,1),
     &    TRANSFER(XT_T2,1), TRANSFER(XT_D1,1),
     &    TRANSFER(XT_D2,1), TRANSFER(XT_U1,1),
     &    TRANSFER(XT_U2,1), TRANSFER(XT_X1,1),
     &    TRANSFER(XT_X2,1)
      ENDIF
C
      WF1    = 1.0 - WF2
      WF1_A1 = -WF2_A1
      WF1_X1 = -WF2_X1
      WF1_X2 = -WF2_X2
      WF1_T1 = -WF2_T1
      WF1_T2 = -WF2_T2
      WF1_D1 = -WF2_D1
      WF1_D2 = -WF2_D2
      WF1_U1 = -WF2_U1
      WF1_U2 = -WF2_U2
      WF1_MS = -WF2_MS
      WF1_RE = -WF2_RE
      WF1_XF = -WF2_XF
C
C
C**** FIRST,  do laminar part between X1 and XT
C
C-----interpolate primary variables to transition point
      IF(TRACE_SIDE.EQ.2 .AND. TRACE_STATION.EQ.66
     &   .AND. TRACE_OUTER.EQ.10 .AND. TRACE_ITER.EQ.2) THEN
        WRITE(0,'(A,6(1X,A,Z8.8))') 'F_TTIN_N66',
     &   'T1=',TRANSFER(T1,1),'T2=',TRANSFER(T2,1),
     &   'WF1_A1=',TRANSFER(WF1_A1,1),'WF2_A1=',TRANSFER(WF2_A1,1),
     &   'WF1=',TRANSFER(WF1,1),'WF2=',TRANSFER(WF2,1)
      ENDIF
      TT    = T1*WF1    + T2*WF2
      TT_A1 = T1*WF1_A1 + T2*WF2_A1
      TT_X1 = T1*WF1_X1 + T2*WF2_X1
      TT_X2 = T1*WF1_X2 + T2*WF2_X2
      TT_T1 = T1*WF1_T1 + T2*WF2_T1 + WF1
      TT_T2 = T1*WF1_T2 + T2*WF2_T2 + WF2
      TT_D1 = T1*WF1_D1 + T2*WF2_D1
      TT_D2 = T1*WF1_D2 + T2*WF2_D2
      TT_U1 = T1*WF1_U1 + T2*WF2_U1
      TT_U2 = T1*WF1_U2 + T2*WF2_U2
      TT_MS = T1*WF1_MS + T2*WF2_MS
      TT_RE = T1*WF1_RE + T2*WF2_RE
      TT_XF = T1*WF1_XF + T2*WF2_XF
      IF(TRACE_SIDE.EQ.2 .AND. TRACE_STATION.EQ.5
     &   .AND. TRACE_ITER.EQ.1) THEN
        WRITE(0,'(A,8(1X,Z8.8))') 'G_TDU_TT',
     &    TRANSFER(TT,1), TRANSFER(TT_A1,1),
     &    TRANSFER(TT_T1,1), TRANSFER(TT_T2,1),
     &    TRANSFER(TT_D1,1), TRANSFER(TT_D2,1),
     &    TRANSFER(TT_U1,1), TRANSFER(TT_U2,1)
      ENDIF
      IF(TRACE_SIDE.EQ.2 .AND. TRACE_STATION.EQ.50) THEN
       WRITE(0,'(A,Z8,1X,Z8,1X,Z8,1X,Z8,1X,Z8,1X,Z8)')
     &  'F_TP50_SENS ',
     &  TRANSFER(TT_D1,1),TRANSFER(DT_D1,1),
     &  TRANSFER(UT_D1,1),TRANSFER(TT_T1,1),
     &  TRANSFER(DT_T1,1),TRANSFER(UT_T1,1)
      ENDIF
C
      DT    = D1*WF1    + D2*WF2
      DT_A1 = D1*WF1_A1 + D2*WF2_A1
      DT_X1 = D1*WF1_X1 + D2*WF2_X1
      DT_X2 = D1*WF1_X2 + D2*WF2_X2
      DT_T1 = D1*WF1_T1 + D2*WF2_T1
      DT_T2 = D1*WF1_T2 + D2*WF2_T2
      DT_D1 = D1*WF1_D1 + D2*WF2_D1 + WF1
      DT_D2 = D1*WF1_D2 + D2*WF2_D2 + WF2
      DT_U1 = D1*WF1_U1 + D2*WF2_U1
      DT_U2 = D1*WF1_U2 + D2*WF2_U2
      DT_MS = D1*WF1_MS + D2*WF2_MS
      DT_RE = D1*WF1_RE + D2*WF2_RE
      DT_XF = D1*WF1_XF + D2*WF2_XF
C
      UT    = U1*WF1    + U2*WF2
      IF(TRACE_SIDE.EQ.2 .AND. TRACE_STATION.EQ.66
     &   .AND. TRACE_ITER.EQ.2 .AND. TRACE_OUTER.EQ.10) THEN
       WRITE(0,'(A,4(A,Z8))') 'F_TRPT10_66',
     &  ' XT=',TRANSFER(XT,1),
     &  ' UT=',TRANSFER(UT,1),
     &  ' TT=',TRANSFER(TT,1),
     &  ' DT=',TRANSFER(DT,1)
       WRITE(0,'(A,4(A,Z8))') 'F_TRPT10_66',
     &  ' WF1=',TRANSFER(WF1,1),
     &  ' WF2=',TRANSFER(WF2,1),
     &  ' T1=',TRANSFER(T1,1),
     &  ' T2=',TRANSFER(T2,1)
      ENDIF
      UT_A1 = U1*WF1_A1 + U2*WF2_A1
      UT_X1 = U1*WF1_X1 + U2*WF2_X1
      UT_X2 = U1*WF1_X2 + U2*WF2_X2
      UT_T1 = U1*WF1_T1 + U2*WF2_T1
      UT_T2 = U1*WF1_T2 + U2*WF2_T2
      UT_D1 = U1*WF1_D1 + U2*WF2_D1
      UT_D2 = U1*WF1_D2 + U2*WF2_D2
      UT_U1 = U1*WF1_U1 + U2*WF2_U1 + WF1
      UT_U2 = U1*WF1_U2 + U2*WF2_U2 + WF2
      UT_MS = U1*WF1_MS + U2*WF2_MS
      UT_RE = U1*WF1_RE + U2*WF2_RE
      UT_XF = U1*WF1_XF + U2*WF2_XF
      IF(TRACE_SIDE.EQ.1 .AND. TRACE_STATION.EQ.78) THEN
        WRITE(0,'(A,6(A,Z8))')
     &   'F_TRDIFX2_78',
     &   ' TT_X2=',TRANSFER(TT_X2,1),
     &   ' DT_X2=',TRANSFER(DT_X2,1),
     &   ' UT_X2=',TRANSFER(UT_X2,1),
     &   ' XT_X2=',TRANSFER(XT_X2,1),
     &   ' WF2=',TRANSFER(WF2,1),
     &   ' WF2X2=',TRANSFER(WF2_X2,1)
      ENDIF
      IF(TRACE_SIDE.EQ.2 .AND. TRACE_STATION.EQ.80) THEN
        WRITE(0,'(A,6(A,Z8))')
     &   'F_TRDIFX2_80',
     &   ' TT_X2=',TRANSFER(TT_X2,1),
     &   ' DT_X2=',TRANSFER(DT_X2,1),
     &   ' UT_X2=',TRANSFER(UT_X2,1),
     &   ' XT_X2=',TRANSFER(XT_X2,1),
     &   ' WF2=',TRANSFER(WF2,1),
     &   ' WF2X2=',TRANSFER(WF2_X2,1)
      ENDIF
      IF(TRACE_SIDE.EQ.2 .AND. TRACE_STATION.EQ.5
     &   .AND. TRACE_ITER.EQ.1) THEN
        WRITE(0,'(A,8(1X,Z8.8))') 'G_TDU_DT',
     &    TRANSFER(DT,1), TRANSFER(DT_A1,1),
     &    TRANSFER(DT_T1,1), TRANSFER(DT_T2,1),
     &    TRANSFER(DT_D1,1), TRANSFER(DT_D2,1),
     &    TRANSFER(DT_U1,1), TRANSFER(DT_U2,1)
        WRITE(0,'(A,8(1X,Z8.8))') 'G_TDU_UT',
     &    TRANSFER(UT,1), TRANSFER(UT_A1,1),
     &    TRANSFER(UT_T1,1), TRANSFER(UT_T2,1),
     &    TRANSFER(UT_D1,1), TRANSFER(UT_D2,1),
     &    TRANSFER(UT_U1,1), TRANSFER(UT_U2,1)
      ENDIF
C
      IF(TRACE_SIDE.EQ.2 .AND. TRACE_STATION.EQ.50
     &   .AND. TRACE_ITER.EQ.1) THEN
        WRITE(0,'(A,Z8,A,Z8,A,Z8,A,Z8,A,Z8,A,Z8)')
     &   'F_TRDIF50 WF2=',TRANSFER(WF2,1),
     &   ' TT=',TRANSFER(TT,1),
     &   ' DT=',TRANSFER(DT,1),
     &   ' UT=',TRANSFER(UT,1),
     &   ' T1=',TRANSFER(T1,1),
     &   ' T2=',TRANSFER(T2,1)
      ENDIF
      IF(TRACE_SIDE.EQ.1 .AND. TRACE_STATION.EQ.58
     &   .AND. TRACE_ITER.EQ.1) THEN
        WRITE(*,'(A,6(1X,Z8.8))')
     &   'F_TRDER58',
     &   TRANSFER(ST_T2,1), TRANSFER(TT_T2,1),
     &   TRANSFER(DT_T2,1), TRANSFER(UT_T2,1),
     &   TRANSFER(XT_T2,1), TRANSFER(WF2,1)
      ENDIF
C---- set primary "T" variables at XT  (really placed into "2" variables)
      X2 = XT
      T2 = TT
      D2 = DT
      U2 = UT
C
      AMPL2 = AMCRIT
      S2 = 0.
C
C---- calculate laminar secondary "T" variables
      CALL BLKIN
      CALL BLVAR(1)
C
C---- calculate X1-XT midpoint CFM value
      CALL BLMID(1)
C=
C=    at this point, all "2" variables are really "T" variables at XT
C=
C
C---- Trace laminar BLDIF inputs at station 5 side 2
      IF(TRACE_SIDE.EQ.2 .AND. TRACE_STATION.EQ.5) THEN
       WRITE(*,'(A,6(1X,Z8.8))')
     &  'F_XTSENS5',
     &  TRANSFER(XT_A1,1),TRANSFER(XT_T1,1),
     &  TRANSFER(XT_D1,1),TRANSFER(XT_U1,1),
     &  TRANSFER(DT_A1,1),TRANSFER(WF2_A1,1)
       WRITE(*,'(A,9(1X,Z8.8))')
     &  'F_LAM5',
     &  TRANSFER(X1,1),TRANSFER(X2,1),
     &  TRANSFER(T1,1),TRANSFER(T2,1),
     &  TRANSFER(D1,1),TRANSFER(D2,1),
     &  TRANSFER(U1,1),TRANSFER(U2,1),
     &  TRANSFER(HK2,1)
       WRITE(*,'(A,7(1X,Z8.8))')
     &  'F_KIN1_5',
     &  TRANSFER(HK1,1),TRANSFER(HK1_T1,1),
     &  TRANSFER(HK1_D1,1),TRANSFER(RT1,1),
     &  TRANSFER(RT1_T1,1),TRANSFER(M1,1),
     &  TRANSFER(H1,1)
       WRITE(*,'(A,4(1X,Z8.8))')
     &  'F_SEC1_5',
     &  TRANSFER(HS1,1),TRANSFER(CF1,1),
     &  TRANSFER(DI1,1),TRANSFER(US1,1)
      ENDIF
C---- n6h20 sensitivity dump: transition point A1/T1/U1 derivatives
      IF(TRACE_SIDE.EQ.2 .AND. TRACE_STATION.EQ.66
     &   .AND. TRACE_OUTER.EQ.10 .AND. TRACE_ITER.EQ.2) THEN
        WRITE(0,'(A,8(1X,A,Z8.8))') 'F_STINPUTS_N66',
     &   'st_Tt=',TRANSFER(ST_TT,1),
     &   'st_Dt=',TRANSFER(ST_DT,1),
     &   'st_Ut=',TRANSFER(ST_UT,1),
     &   'ctr=',TRANSFER(CTR,1),
     &   'cqT=',TRANSFER(CQ2,1),
     &   'cqT_Tt=',TRANSFER(CQ2_T2,1),
     &   'ctr_Hk=',TRANSFER(CTR_HK2,1),
     &   'HK2_T2=',TRANSFER(HK2_T2,1)
        WRITE(0,'(A,5(1X,A,Z8.8))') 'F_SENS_N66_A1',
     &   'ST=',TRANSFER(ST_A1,1),'TT=',TRANSFER(TT_A1,1),
     &   'DT=',TRANSFER(DT_A1,1),'UT=',TRANSFER(UT_A1,1),
     &   'XT=',TRANSFER(XT_A1,1)
        WRITE(0,'(A,5(1X,A,Z8.8))') 'F_SENS_N66_T1',
     &   'ST=',TRANSFER(ST_T1,1),'TT=',TRANSFER(TT_T1,1),
     &   'DT=',TRANSFER(DT_T1,1),'UT=',TRANSFER(UT_T1,1),
     &   'XT=',TRANSFER(XT_T1,1)
        WRITE(0,'(A,5(1X,A,Z8.8))') 'F_SENS_N66_U1',
     &   'ST=',TRANSFER(ST_U1,1),'TT=',TRANSFER(TT_U1,1),
     &   'DT=',TRANSFER(DT_U1,1),'UT=',TRANSFER(UT_U1,1),
     &   'XT=',TRANSFER(XT_U1,1)
      ENDIF
C---- set up Newton system for dAm, dTh, dDs, dUe, dXi  at  X1 and XT
      CALL BLDIF(1)
      IF(TRACE_SIDE.EQ.2 .AND. TRACE_STATION.EQ.24
     &   .AND. TRACE_ITER.EQ.4) THEN
       DO 9193 KL=1, 3
        WRITE(0,'(A,I1,5(1X,Z8))') 'F_LAMVS2 r',KL-1,
     &   TRANSFER(VS2(KL,1),1),TRANSFER(VS2(KL,2),1),
     &   TRANSFER(VS2(KL,3),1),TRANSFER(VS2(KL,4),1),
     &   TRANSFER(VS2(KL,5),1)
        WRITE(0,'(A,I1,5(1X,Z8))') 'F_LAMVS1 r',KL-1,
     &   TRANSFER(VS1(KL,1),1),TRANSFER(VS1(KL,2),1),
     &   TRANSFER(VS1(KL,3),1),TRANSFER(VS1(KL,4),1),
     &   TRANSFER(VS1(KL,5),1)
 9193  CONTINUE
      ENDIF
      IF(TRACE_SIDE.EQ.1 .AND. TRACE_STATION.EQ.78) THEN
       WRITE(0,'(A,4(A,Z8))')
     &  'F_LAMV3A_78',
     &  ' v22=',TRANSFER(VS2(3,2),1),
     &  ' v23=',TRANSFER(VS2(3,3),1),
     &  ' v24=',TRANSFER(VS2(3,4),1),
     &  ' v25=',TRANSFER(VS2(3,5),1)
       WRITE(0,'(A,4(A,Z8))')
     &  'F_LAMV3B_78',
     &  ' tt4=',TRANSFER(TT_X2,1),
     &  ' dt4=',TRANSFER(DT_X2,1),
     &  ' ut4=',TRANSFER(UT_X2,1),
     &  ' xt4=',TRANSFER(XT_X2,1)
      ENDIF
      IF(TRACE_SIDE.EQ.2 .AND. TRACE_STATION.EQ.5) THEN
       WRITE(*,'(A,4(1X,Z8.8))')
     &  'F_LAM_VS21',
     &  TRANSFER(VS2(2,1),1),TRANSFER(VS2(2,2),1),
     &  TRANSFER(VS2(2,3),1),TRANSFER(VS2(2,4),1)
      ENDIF
      IF(TRACE_SIDE.EQ.1 .AND. TRACE_STATION.EQ.78
     &   .AND. TRACE_OUTER.EQ.5) THEN
       WRITE(0,'(A,8(1X,A,Z8))')
     &  'F_LAMV2R3_78',
     &  ' v22=',TRANSFER(VS2(3,2),1),
     &  ' v23=',TRANSFER(VS2(3,3),1),
     &  ' v24=',TRANSFER(VS2(3,4),1),
     &  ' v25=',TRANSFER(VS2(3,5),1),
     &  ' ttX2=',TRANSFER(TT_X2,1),
     &  ' dtX2=',TRANSFER(DT_X2,1),
     &  ' utX2=',TRANSFER(UT_X2,1),
     &  ' xtX2=',TRANSFER(XT_X2,1)
      ENDIF
      IF(TRACE_SIDE.EQ.2 .AND. TRACE_STATION.EQ.50
     &   .AND. TRACE_ITER.EQ.1) THEN
        WRITE(0,'(A,Z8,A,Z8,A,Z8)')
     &   'F_TRDIF_LAM R2=',TRANSFER(VSREZ(2),1),
     &   ' R3=',TRANSFER(VSREZ(3),1),
     &   ' HK2=',TRANSFER(HK2,1)
      ENDIF
C
C---- The current Newton system is in terms of "1" and "T" variables,
C-    so calculate its equivalent in terms of "1" and "2" variables.
C-    In other words, convert residual sensitivities wrt "T" variables
C-    into sensitivities wrt "1" and "2" variables.  The amplification
C-    equation is unnecessary here, so the K=1 row is left empty.
      DO 10 K=2, 3
        BLREZ(K) = VSREZ(K)
        BLM(K)   = VSM(K)
     &           + VS2(K,2)*TT_MS
     &           + VS2(K,3)*DT_MS
     &           + VS2(K,4)*UT_MS
     &           + VS2(K,5)*XT_MS
        BLR(K)   = VSR(K)
     &           + VS2(K,2)*TT_RE
     &           + VS2(K,3)*DT_RE
     &           + VS2(K,4)*UT_RE
     &           + VS2(K,5)*XT_RE
        BLX(K)   = VSX(K)
     &           + VS2(K,2)*TT_XF
     &           + VS2(K,3)*DT_XF
     &           + VS2(K,4)*UT_XF
     &           + VS2(K,5)*XT_XF
C
        IF(K.EQ.2) THEN
         BL1VS121 = VS1(2,1)
         BL1P1 = VS2(2,2)*TT_A1
         BL1P2 = VS2(2,3)*DT_A1
         BL1P3 = VS2(2,4)*UT_A1
         BL1P4 = VS2(2,5)*XT_A1
        ENDIF
        BL1(K,1) = VS1(K,1)
     &           + VS2(K,2)*TT_A1
     &           + VS2(K,3)*DT_A1
     &           + VS2(K,4)*UT_A1
     &           + VS2(K,5)*XT_A1
        BL1(K,2) = VS1(K,2)
     &           + VS2(K,2)*TT_T1
     &           + VS2(K,3)*DT_T1
     &           + VS2(K,4)*UT_T1
     &           + VS2(K,5)*XT_T1
        BL1(K,3) = VS1(K,3)
     &           + VS2(K,2)*TT_D1
     &           + VS2(K,3)*DT_D1
     &           + VS2(K,4)*UT_D1
     &           + VS2(K,5)*XT_D1
        BL1(K,4) = VS1(K,4)
     &           + VS2(K,2)*TT_U1
     &           + VS2(K,3)*DT_U1
     &           + VS2(K,4)*UT_U1
     &           + VS2(K,5)*XT_U1
        BL1(K,5) = VS1(K,5)
     &           + VS2(K,2)*TT_X1
     &           + VS2(K,3)*DT_X1
     &           + VS2(K,4)*UT_X1
     &           + VS2(K,5)*XT_X1
C
        BL2(K,1) = 0.
        BL2(K,2) = VS2(K,2)*TT_T2
     &           + VS2(K,3)*DT_T2
     &           + VS2(K,4)*UT_T2
     &           + VS2(K,5)*XT_T2
        BL2(K,3) = VS2(K,2)*TT_D2
     &           + VS2(K,3)*DT_D2
     &           + VS2(K,4)*UT_D2
     &           + VS2(K,5)*XT_D2
        BL2(K,4) = VS2(K,2)*TT_U2
     &           + VS2(K,3)*DT_U2
     &           + VS2(K,4)*UT_U2
     &           + VS2(K,5)*XT_U2
        BL2(K,5) = VS2(K,2)*TT_X2
     &           + VS2(K,3)*DT_X2
     &           + VS2(K,4)*UT_X2
     &           + VS2(K,5)*XT_X2
C---- n6h20 BL1/BL2 row-0 trace at IBL=66 iter 2 mc=10
        IF(K.EQ.1 .AND. TRACE_SIDE.EQ.2 .AND. TRACE_STATION.EQ.66
     &     .AND. TRACE_ITER.EQ.2) THEN
         WRITE(0,'(A,5(A,Z8))') 'F_BL1_10_66 r0',
     &    ' c1=',TRANSFER(BL1(1,1),1),
     &    ' c2=',TRANSFER(BL1(1,2),1),
     &    ' c3=',TRANSFER(BL1(1,3),1),
     &    ' c4=',TRANSFER(BL1(1,4),1),
     &    ' c5=',TRANSFER(BL1(1,5),1)
         WRITE(0,'(A,5(A,Z8))') 'F_BL2_10_66 r0',
     &    ' c1=',TRANSFER(BL2(1,1),1),
     &    ' c2=',TRANSFER(BL2(1,2),1),
     &    ' c3=',TRANSFER(BL2(1,3),1),
     &    ' c4=',TRANSFER(BL2(1,4),1),
     &    ' c5=',TRANSFER(BL2(1,5),1)
        ENDIF
C
   10 CONTINUE
C
C
C**** SECOND, set up turbulent part between XT and X2  ****
C
C---- calculate equilibrium shear coefficient CQT at transition point
      CALL BLVAR(2)
C
C---- set initial shear coefficient value ST at transition point
C-    ( note that CQ2, CQ2_T2, etc. are really "CQT", "CQT_TT", etc.)
C
      CTR     = CTRCON*EXP(-CTRCEX/(HK2-1.0))
      CTR_HK2 = CTR * CTRCEX/(HK2-1.0)**2
C
c      CTR     = 1.1*EXP(-10.0/HK2**2)
c      CTR_HK2 = CTR * 10.0 * 2.0/HK2**3
C
CCC      CTR = 1.2
CCC      CTR = 0.7
CCC      CTR_HK2 = 0.0
C
      ST    = CTR*CQ2
      ST_TT = CTR*CQ2_T2 + CQ2*CTR_HK2*HK2_T2
      ST_DT = CTR*CQ2_D2 + CQ2*CTR_HK2*HK2_D2
      ST_UT = CTR*CQ2_U2 + CQ2*CTR_HK2*HK2_U2
      ST_MS = CTR*CQ2_MS + CQ2*CTR_HK2*HK2_MS
      ST_RE = CTR*CQ2_RE
C
C---- calculate ST sensitivities wrt the actual "1" and "2" variables
      ST_A1 = ST_TT*TT_A1 + ST_DT*DT_A1 + ST_UT*UT_A1
      ST_X1 = ST_TT*TT_X1 + ST_DT*DT_X1 + ST_UT*UT_X1
      ST_X2 = ST_TT*TT_X2 + ST_DT*DT_X2 + ST_UT*UT_X2
      ST_T1 = ST_TT*TT_T1 + ST_DT*DT_T1 + ST_UT*UT_T1
      ST_T2 = ST_TT*TT_T2 + ST_DT*DT_T2 + ST_UT*UT_T2
      ST_D1 = ST_TT*TT_D1 + ST_DT*DT_D1 + ST_UT*UT_D1
      ST_D2 = ST_TT*TT_D2 + ST_DT*DT_D2 + ST_UT*UT_D2
      ST_U1 = ST_TT*TT_U1 + ST_DT*DT_U1 + ST_UT*UT_U1
      ST_U2 = ST_TT*TT_U2 + ST_DT*DT_U2 + ST_UT*UT_U2
      ST_MS = ST_TT*TT_MS + ST_DT*DT_MS + ST_UT*UT_MS + ST_MS
      ST_RE = ST_TT*TT_RE + ST_DT*DT_RE + ST_UT*UT_RE + ST_RE
      ST_XF = ST_TT*TT_XF + ST_DT*DT_XF + ST_UT*UT_XF
C---- n6h20 ST inputs and A1/T1/U1 dump AFTER ST_A1 is fully assembled
      IF(TRACE_SIDE.EQ.2 .AND. TRACE_STATION.EQ.66
     &   .AND. TRACE_OUTER.EQ.10 .AND. TRACE_ITER.EQ.2) THEN
        WRITE(0,'(A,8(1X,A,Z8.8))') 'F2_STINPUTS_N66',
     &   'st_Tt=',TRANSFER(ST_TT,1),
     &   'st_Dt=',TRANSFER(ST_DT,1),
     &   'st_Ut=',TRANSFER(ST_UT,1),
     &   'ctr=',TRANSFER(CTR,1),
     &   'cqT=',TRANSFER(CQ2,1),
     &   'cqT_Tt=',TRANSFER(CQ2_T2,1),
     &   'ctr_Hk=',TRANSFER(CTR_HK2,1),
     &   'HK2_T2=',TRANSFER(HK2_T2,1)
        WRITE(0,'(A,5(1X,A,Z8.8))') 'F2_SENS_N66_A1',
     &   'ST=',TRANSFER(ST_A1,1),'TT=',TRANSFER(TT_A1,1),
     &   'DT=',TRANSFER(DT_A1,1),'UT=',TRANSFER(UT_A1,1),
     &   'XT=',TRANSFER(XT_A1,1)
        WRITE(0,'(A,5(1X,A,Z8.8))') 'F2_SENS_N66_T1',
     &   'ST=',TRANSFER(ST_T1,1),'TT=',TRANSFER(TT_T1,1),
     &   'DT=',TRANSFER(DT_T1,1),'UT=',TRANSFER(UT_T1,1),
     &   'XT=',TRANSFER(XT_T1,1)
      ENDIF
C---- TRDIF chain variables at side 2 station 73 (NACA 0009 5M debug, post BLVAR(2), SETBL only)
      IF(TRACE_SIDE.EQ.2 .AND. TRACE_STATION.EQ.73
     &   .AND. TRACE_PHASE.EQ.1) THEN
       WRITE(0,'(A,6(A,Z8))')
     &  'F_TRD73_W',
     &  ' WF1=',TRANSFER(REAL(WF1),1),
     &  ' WF2=',TRANSFER(REAL(WF2),1),
     &  ' WF2_T1=',TRANSFER(REAL(WF2_T1),1),
     &  ' WF2_T2=',TRANSFER(REAL(WF2_T2),1),
     &  ' WF2_D1=',TRANSFER(REAL(WF2_D1),1),
     &  ' WF2_D2=',TRANSFER(REAL(WF2_D2),1)
       WRITE(0,'(A,6(A,Z8))')
     &  'F_TRD73_TT',
     &  ' TT=',TRANSFER(REAL(TT),1),
     &  ' TT_T1=',TRANSFER(REAL(TT_T1),1),
     &  ' TT_T2=',TRANSFER(REAL(TT_T2),1),
     &  ' TT_D1=',TRANSFER(REAL(TT_D1),1),
     &  ' TT_D2=',TRANSFER(REAL(TT_D2),1),
     &  ' TT_A1=',TRANSFER(REAL(TT_A1),1)
       WRITE(0,'(A,6(A,Z8))')
     &  'F_TRD73_DT',
     &  ' DT=',TRANSFER(REAL(DT),1),
     &  ' DT_T1=',TRANSFER(REAL(DT_T1),1),
     &  ' DT_T2=',TRANSFER(REAL(DT_T2),1),
     &  ' DT_D1=',TRANSFER(REAL(DT_D1),1),
     &  ' DT_D2=',TRANSFER(REAL(DT_D2),1),
     &  ' DT_A1=',TRANSFER(REAL(DT_A1),1)
       WRITE(0,'(A,6(A,Z8))')
     &  'F_TRD73_UT',
     &  ' UT=',TRANSFER(REAL(UT),1),
     &  ' UT_T1=',TRANSFER(REAL(UT_T1),1),
     &  ' UT_T2=',TRANSFER(REAL(UT_T2),1),
     &  ' UT_D1=',TRANSFER(REAL(UT_D1),1),
     &  ' UT_D2=',TRANSFER(REAL(UT_D2),1),
     &  ' UT_A1=',TRANSFER(REAL(UT_A1),1)
       WRITE(0,'(A,6(A,Z8))')
     &  'F_TRD73_XT',
     &  ' XT=',TRANSFER(REAL(XT),1),
     &  ' XT_T1=',TRANSFER(REAL(XT_T1),1),
     &  ' XT_T2=',TRANSFER(REAL(XT_T2),1),
     &  ' XT_D1=',TRANSFER(REAL(XT_D1),1),
     &  ' XT_D2=',TRANSFER(REAL(XT_D2),1),
     &  ' XT_A1=',TRANSFER(REAL(XT_A1),1)
       WRITE(0,'(A,6(A,Z8))')
     &  'F_TRD73_ST',
     &  ' ST=',TRANSFER(REAL(ST),1),
     &  ' ST_T1=',TRANSFER(REAL(ST_T1),1),
     &  ' ST_T2=',TRANSFER(REAL(ST_T2),1),
     &  ' ST_D1=',TRANSFER(REAL(ST_D1),1),
     &  ' ST_D2=',TRANSFER(REAL(ST_D2),1),
     &  ' ST_A1=',TRANSFER(REAL(ST_A1),1)
       WRITE(0,'(A,5(A,Z8))')
     &  'F_TRD73_CQ',
     &  ' CQ2=',TRANSFER(REAL(CQ2),1),
     &  ' CTR=',TRANSFER(REAL(CTR),1),
     &  ' HK2=',TRANSFER(REAL(HK2),1),
     &  ' RT2=',TRANSFER(REAL(RT2),1),
     &  ' CTR_HK2=',TRANSFER(REAL(CTR_HK2),1)
      ENDIF
C
      CALL HST(HK2, RT2, M2,
     &         TRACE_HS2, TRACE_HS2_HK2, TRACE_HS2_RT2, TRACE_HS2_M2)
      TRACE_US2_HS2 = US2_HS2
      TRACE_US2_HK2 = US2_HK2
      TRACE_US2_H2 = US2_H2
      TRACE_US2_THS2 = US2_HS2*HS2_T2
      TRACE_US2_THK2 = US2_HK2*HK2_T2
      TRACE_US2_TH2 = US2_H2*H2_T2
      TRACE_US2_DHS2 = US2_HS2*HS2_D2
      TRACE_US2_DHK2 = US2_HK2*HK2_D2
      TRACE_US2_DH2 = US2_H2*H2_D2
      CALL TRACE_TRANSITION_INTERVAL_US2_TERMS('TRDIF',
     & US2, TRACE_US2_HS2, TRACE_US2_HK2, TRACE_US2_H2,
     & TRACE_US2_THS2, TRACE_US2_THK2, TRACE_US2_TH2,
     & TRACE_US2_DHS2, TRACE_US2_DHK2, TRACE_US2_DH2,
     & US2_T2, US2_D2)
      CALL TRACE_TRANSITION_INTERVAL_ST_TERMS('TRDIF',
     & TRACE_HS2, TRACE_HS2_HK2, TRACE_HS2_RT2, HS2_T2, HS2_D2, HS2_U2,
     & US2, TRACE_US2_HS2, TRACE_US2_HK2, TRACE_US2_H2,
     & US2_T2, US2_D2, US2_U2,
     & TRACE_US2_THS2, TRACE_US2_THK2, TRACE_US2_TH2,
     & TRACE_US2_DHS2, TRACE_US2_DHK2, TRACE_US2_DH2,
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
C
      AMPL2 = 0.
      S2 = ST
C
C---- recalculate turbulent secondary "T" variables using proper CTI
      CALL BLVAR(2)
C
C---- set "1" variables to "T" variables and reset "2" variables
C-    to their saved turbulent values
      DO 30 ICOM=1, NCOM
        COM1(ICOM) = COM2(ICOM)
        COM2(ICOM) = C2SAV(ICOM)
   30 CONTINUE
C
C---- calculate XT-X2 midpoint CFM value
      CALL BLMID(2)
C
C---- set up Newton system for dCt, dTh, dDs, dUe, dXi  at  XT and X2
      CALL BLDIF(2)
      IF(TRACE_SIDE.EQ.2 .AND. TRACE_STATION.EQ.50
     &   .AND. TRACE_ITER.EQ.1) THEN
        WRITE(0,'(A,Z8,A,Z8,A,Z8)')
     &   'F_TRDIF_TRB R1=',TRANSFER(VSREZ(1),1),
     &   ' R2=',TRANSFER(VSREZ(2),1),
     &   ' R3=',TRANSFER(VSREZ(3),1)
      ENDIF
C---- Station 73 side 2 turbulent BLDIF(2) output (row 1 shear lag) for NACA 0009 5M
      IF(TRACE_SIDE.EQ.2 .AND. TRACE_STATION.EQ.73
     &   .AND. TRACE_PHASE.EQ.1) THEN
        WRITE(0,'(A,10(A,Z8))')
     &   'F_TURBVS_73',
     &   ' VS10=',TRANSFER(REAL(VS1(1,1)),1),
     &   ' VS11=',TRANSFER(REAL(VS1(1,2)),1),
     &   ' VS12=',TRANSFER(REAL(VS1(1,3)),1),
     &   ' VS13=',TRANSFER(REAL(VS1(1,4)),1),
     &   ' VS14=',TRANSFER(REAL(VS1(1,5)),1),
     &   ' VS20=',TRANSFER(REAL(VS2(1,1)),1),
     &   ' VS21=',TRANSFER(REAL(VS2(1,2)),1),
     &   ' VS22=',TRANSFER(REAL(VS2(1,3)),1),
     &   ' VS23=',TRANSFER(REAL(VS2(1,4)),1),
     &   ' VS24=',TRANSFER(REAL(VS2(1,5)),1)
      ENDIF
C---- Station 73 side 2: after BT1 row 1 assembly, dump BT1(1,1..3) (SETBL only)
      IF(TRACE_SIDE.EQ.2 .AND. TRACE_STATION.EQ.73
     &   .AND. TRACE_PHASE.EQ.1) THEN
       F_BT10_T1 = VS1(1,1)*ST_A1 + VS1(1,2)*TT_A1
     &           + VS1(1,3)*DT_A1 + VS1(1,4)*UT_A1 + VS1(1,5)*XT_A1
       F_BT11_T1 = VS1(1,1)*ST_T1 + VS1(1,2)*TT_T1
     &           + VS1(1,3)*DT_T1 + VS1(1,4)*UT_T1 + VS1(1,5)*XT_T1
       F_BT12_T1 = VS1(1,1)*ST_D1 + VS1(1,2)*TT_D1
     &           + VS1(1,3)*DT_D1 + VS1(1,4)*UT_D1 + VS1(1,5)*XT_D1
       WRITE(0,'(A,8(A,Z8))')
     &  'F_BT10_73',
     &  ' bt10=',TRANSFER(F_BT10_T1,1),
     &  ' bt11=',TRANSFER(F_BT11_T1,1),
     &  ' bt12=',TRANSFER(F_BT12_T1,1),
     &  ' vs10=',TRANSFER(VS1(1,1),1),
     &  ' vs11=',TRANSFER(VS1(1,2),1),
     &  ' vs12=',TRANSFER(VS1(1,3),1),
     &  ' vs13=',TRANSFER(VS1(1,4),1),
     &  ' vs14=',TRANSFER(VS1(1,5),1)
       WRITE(0,'(A,5(A,Z8))')
     &  'F_BT10_73_SENS',
     &  ' stA1=',TRANSFER(ST_A1,1),
     &  ' ttA1=',TRANSFER(TT_A1,1),
     &  ' dtA1=',TRANSFER(DT_A1,1),
     &  ' utA1=',TRANSFER(UT_A1,1),
     &  ' xtA1=',TRANSFER(XT_A1,1)
      ENDIF
C---- Station 78 side 1 outer iter 3: turbulent BLDIF output (row 1 shear lag)
      IF(TRACE_SIDE.EQ.1 .AND. TRACE_STATION.EQ.78
     &   .AND. TRACE_OUTER.EQ.3) THEN
        WRITE(0,'(A,10(A,Z8))')
     &   'F_TURBVS_78',
     &   ' VS10=',TRANSFER(REAL(VS1(1,1)),1),
     &   ' VS11=',TRANSFER(REAL(VS1(1,2)),1),
     &   ' VS12=',TRANSFER(REAL(VS1(1,3)),1),
     &   ' VS13=',TRANSFER(REAL(VS1(1,4)),1),
     &   ' VS14=',TRANSFER(REAL(VS1(1,5)),1),
     &   ' VS20=',TRANSFER(REAL(VS2(1,1)),1),
     &   ' VS21=',TRANSFER(REAL(VS2(1,2)),1),
     &   ' VS22=',TRANSFER(REAL(VS2(1,3)),1),
     &   ' VS23=',TRANSFER(REAL(VS2(1,4)),1),
     &   ' VS24=',TRANSFER(REAL(VS2(1,5)),1)
      ENDIF
C
C---- convert sensitivities wrt "T" variables into sensitivities
C-    wrt "1" and "2" variables as done before for the laminar part
      DO 40 K=1, 3
        BTREZ(K) = VSREZ(K)
        BTM(K)   = VSM(K)
     &           + VS1(K,1)*ST_MS
     &           + VS1(K,2)*TT_MS
     &           + VS1(K,3)*DT_MS
     &           + VS1(K,4)*UT_MS
     &           + VS1(K,5)*XT_MS
        IF(K.EQ.1 .AND. TRACE_SIDE.EQ.1 .AND.
     &     TRACE_STATION.EQ.79) THEN
         WRITE(*,'(A,Z8,A,Z8,A,Z8,A,Z8,A,Z8,A,Z8)')
     &    'F_BTX_TERMS VSX=',TRANSFER(VSX(1),1),
     &    ' V11=',TRANSFER(VS1(1,1),1),
     &    ' ST_XF=',TRANSFER(ST_XF,1),
     &    ' TT_XF=',TRANSFER(TT_XF,1),
     &    ' UT_XF=',TRANSFER(UT_XF,1),
     &    ' XT_XF=',TRANSFER(XT_XF,1)
        ENDIF
        BTR(K)   = VSR(K)
     &           + VS1(K,1)*ST_RE
     &           + VS1(K,2)*TT_RE
     &           + VS1(K,3)*DT_RE
     &           + VS1(K,4)*UT_RE
     &           + VS1(K,5)*XT_RE
        BTX(K)   = VSX(K)
     &           + VS1(K,1)*ST_XF
     &           + VS1(K,2)*TT_XF
     &           + VS1(K,3)*DT_XF
     &           + VS1(K,4)*UT_XF
     &           + VS1(K,5)*XT_XF
        IF(K.EQ.3 .AND. TRACE_SIDE.EQ.1 .AND. TRACE_STATION.EQ.78) THEN
         WRITE(0,'(A,5(1X,Z8.8),1X,5(1X,Z8.8))')
     &    'F_BTX78R3',
     &    TRANSFER(VS1(3,1),1),TRANSFER(VS1(3,2),1),
     &    TRANSFER(VS1(3,3),1),TRANSFER(VS1(3,4),1),
     &    TRANSFER(VS1(3,5),1),
     &    TRANSFER(ST_XF,1),TRANSFER(TT_XF,1),TRANSFER(DT_XF,1),
     &    TRANSFER(UT_XF,1),TRANSFER(XT_XF,1)
        ENDIF
C
        BT1(K,1) = VS1(K,1)*ST_A1
     &           + VS1(K,2)*TT_A1
     &           + VS1(K,3)*DT_A1
     &           + VS1(K,4)*UT_A1
     &           + VS1(K,5)*XT_A1
        BT1(K,2) = VS1(K,1)*ST_T1
     &           + VS1(K,2)*TT_T1
     &           + VS1(K,3)*DT_T1
     &           + VS1(K,4)*UT_T1
     &           + VS1(K,5)*XT_T1
        BT1(K,3) = VS1(K,1)*ST_D1
     &           + VS1(K,2)*TT_D1
     &           + VS1(K,3)*DT_D1
     &           + VS1(K,4)*UT_D1
     &           + VS1(K,5)*XT_D1
        IF(K.EQ.1 .AND. TRACE_SIDE.EQ.2 .AND.
     &     TRACE_STATION.EQ.5) THEN
         WRITE(0,'(A,5(1X,Z8.8),A,5(1X,Z8.8),A,Z8)')
     &    'F_BT12_5',
     &    TRANSFER(VS1(1,1),1),TRANSFER(ST_D1,1),
     &    TRANSFER(VS1(1,2),1),TRANSFER(TT_D1,1),
     &    TRANSFER(VS1(1,3),1),
     &    ' |',TRANSFER(DT_D1,1),
     &    TRANSFER(VS1(1,4),1),TRANSFER(UT_D1,1),
     &    TRANSFER(VS1(1,5),1),TRANSFER(XT_D1,1),
     &    ' R=',TRANSFER(BT1(1,3),1)
        ENDIF
        IF(K.EQ.2 .AND. TRACE_SIDE.EQ.2 .AND.
     &     TRACE_STATION.EQ.50) THEN
         WRITE(0,'(A,Z8,1X,Z8,1X,Z8,1X,Z8,1X,Z8)')
     &    'F_BT12_INPUTS ',
     &    TRANSFER(VS1(2,1),1),TRANSFER(ST_D1,1),
     &    TRANSFER(VS1(2,2),1),TRANSFER(TT_D1,1),
     &    TRANSFER(VS1(2,3),1)
         WRITE(0,'(A,Z8,1X,Z8,1X,Z8,1X,Z8,1X,Z8)')
     &    'F_BT12_INPUTS2 ',
     &    TRANSFER(DT_D1,1),TRANSFER(VS1(2,4),1),
     &    TRANSFER(UT_D1,1),TRANSFER(VS1(2,5),1),
     &    TRANSFER(XT_D1,1)
         WRITE(0,'(A,Z8)')
     &    'F_BT12_RESULT ',TRANSFER(BT1(2,3),1)
        ENDIF
        BT1(K,4) = VS1(K,1)*ST_U1
     &           + VS1(K,2)*TT_U1
     &           + VS1(K,3)*DT_U1
     &           + VS1(K,4)*UT_U1
     &           + VS1(K,5)*XT_U1
        BT1(K,5) = VS1(K,1)*ST_X1
     &           + VS1(K,2)*TT_X1
     &           + VS1(K,3)*DT_X1
     &           + VS1(K,4)*UT_X1
     &           + VS1(K,5)*XT_X1
C
        BT2(K,1) = VS2(K,1)
        BT2(K,2) = VS2(K,2)
     &           + VS1(K,1)*ST_T2
     &           + VS1(K,2)*TT_T2
     &           + VS1(K,3)*DT_T2
     &           + VS1(K,4)*UT_T2
     &           + VS1(K,5)*XT_T2
        BT2(K,3) = VS2(K,3)
     &           + VS1(K,1)*ST_D2
     &           + VS1(K,2)*TT_D2
     &           + VS1(K,3)*DT_D2
     &           + VS1(K,4)*UT_D2
     &           + VS1(K,5)*XT_D2
        BT2(K,4) = VS2(K,4)
     &           + VS1(K,1)*ST_U2
     &           + VS1(K,2)*TT_U2
     &           + VS1(K,3)*DT_U2
     &           + VS1(K,4)*UT_U2
     &           + VS1(K,5)*XT_U2
        BT2(K,5) = VS2(K,5)
     &           + VS1(K,1)*ST_X2
     &           + VS1(K,2)*TT_X2
     &           + VS1(K,3)*DT_X2
     &           + VS1(K,4)*UT_X2
     &           + VS1(K,5)*XT_X2
        IF(K.EQ.1 .AND. TRACE_SIDE.EQ.2 .AND. TRACE_STATION.EQ.69
     &      .AND. TRACE_ITER.EQ.33) THEN
         WRITE(0,'(A,11(1X,A,Z8.8))') 'F_BT24_69',
     &    'base=',TRANSFER(REAL(VS2(1,5)),1),
     &    'vs10=',TRANSFER(REAL(VS1(1,1)),1),
     &    'st4=',TRANSFER(REAL(ST_X2),1),
     &    'vs11=',TRANSFER(REAL(VS1(1,2)),1),
     &    'tt4=',TRANSFER(REAL(TT_X2),1),
     &    'vs12=',TRANSFER(REAL(VS1(1,3)),1),
     &    'dt4=',TRANSFER(REAL(DT_X2),1),
     &    'vs13=',TRANSFER(REAL(VS1(1,4)),1),
     &    'ut4=',TRANSFER(REAL(UT_X2),1),
     &    'vs14=',TRANSFER(REAL(VS1(1,5)),1),
     &    'xt4=',TRANSFER(REAL(XT_X2),1)
        ENDIF
C---- n6h20 BT2 row-0 trace at IBL=66 iter 2 mc=10
        IF(K.EQ.1 .AND. TRACE_SIDE.EQ.2 .AND. TRACE_STATION.EQ.66
     &     .AND. TRACE_ITER.EQ.2 .AND. TRACE_OUTER.EQ.10) THEN
         WRITE(0,'(A,5(A,Z8))') 'F_BT2_10_66 r0',
     &    ' c1=',TRANSFER(BT2(1,1),1),
     &    ' c2=',TRANSFER(BT2(1,2),1),
     &    ' c3=',TRANSFER(BT2(1,3),1),
     &    ' c4=',TRANSFER(BT2(1,4),1),
     &    ' c5=',TRANSFER(BT2(1,5),1)
         WRITE(0,'(A,5(A,Z8))') 'F_VS12_AT_BT2',
     &    ' VS12_0=',TRANSFER(VS1(1,1),1),
     &    ' VS12_1=',TRANSFER(VS1(1,2),1),
     &    ' VS12_2=',TRANSFER(VS1(1,3),1),
     &    ' VS12_3=',TRANSFER(VS1(1,4),1),
     &    ' VS12_4=',TRANSFER(VS1(1,5),1)
         WRITE(0,'(A,4(A,Z8))') 'F_DER12_T2',
     &    ' ST_T2=',TRANSFER(ST_T2,1),
     &    ' TT_T2=',TRANSFER(TT_T2,1),
     &    ' DT_T2=',TRANSFER(DT_T2,1),
     &    ' UT_T2=',TRANSFER(UT_T2,1)
         WRITE(0,'(A,4(A,Z8))') 'F_XT_DERIVS',
     &    ' XT_T2=',TRANSFER(XT_T2,1),
     &    ' XT_D2=',TRANSFER(XT_D2,1),
     &    ' XT_U2=',TRANSFER(XT_U2,1),
     &    ' XT_X2=',TRANSFER(XT_X2,1)
        ENDIF
        IF(K.EQ.1 .AND. TRACE_SIDE.EQ.2 .AND.
     &     TRACE_STATION.EQ.80 .AND. TRACE_PHASE.EQ.1) THEN
         WRITE(0,'(A,Z8,A,Z8,A,Z8,A,Z8,A,Z8,A,Z8,A,Z8,
     &    A,Z8,A,Z8,A,Z8,A,Z8,A,Z8)')
     &    'F_BT24_80 bt24=',TRANSFER(BT2(1,5),1),
     &    ' VS25=',TRANSFER(VS2(1,5),1),
     &    ' V10=',TRANSFER(VS1(1,1),1),
     &    ' stX2=',TRANSFER(ST_X2,1),
     &    ' V11=',TRANSFER(VS1(1,2),1),
     &    ' ttX2=',TRANSFER(TT_X2,1),
     &    ' V12=',TRANSFER(VS1(1,3),1),
     &    ' dtX2=',TRANSFER(DT_X2,1),
     &    ' V13=',TRANSFER(VS1(1,4),1),
     &    ' utX2=',TRANSFER(UT_X2,1),
     &    ' V14=',TRANSFER(VS1(1,5),1),
     &    ' xtX2=',TRANSFER(XT_X2,1)
        ENDIF
        IF(K.EQ.3 .AND. TRACE_SIDE.EQ.1 .AND.
     &     TRACE_STATION.EQ.78) THEN
         WRITE(0,'(A,11(A,Z8))')
     &    'F_BT24_78',
     &    ' base=',TRANSFER(VS2(3,5),1),
     &    ' vs10=',TRANSFER(VS1(3,1),1),
     &    ' stX2=',TRANSFER(ST_X2,1),
     &    ' vs11=',TRANSFER(VS1(3,2),1),
     &    ' ttX2=',TRANSFER(TT_X2,1),
     &    ' vs12=',TRANSFER(VS1(3,3),1),
     &    ' dtX2=',TRANSFER(DT_X2,1),
     &    ' vs13=',TRANSFER(VS1(3,4),1),
     &    ' utX2=',TRANSFER(UT_X2,1),
     &    ' vs14=',TRANSFER(VS1(3,5),1),
     &    ' xtX2=',TRANSFER(XT_X2,1),
     &    ' bt24=',TRANSFER(BT2(3,5),1)
        ENDIF
C
   40 CONTINUE
C
C     BT2TT trace disabled
      CALL TRACE_TRANSITION_INTERVAL_BT2_D_TERMS('TRDIF',
     & VS2(1,3),
     & VS1(1,1)*ST_D2, VS1(1,2)*TT_D2, VS1(1,3)*DT_D2,
     & VS1(1,4)*UT_D2, VS1(1,5)*XT_D2,
     & BT2(1,3))
      CALL TRACE_TRANSITION_INTERVAL_BT2_TERMS('TRDIF',
     & TRACE_SIDE, TRACE_STATION,
     & 1, 2,
     & VS2(1,2),
     & VS1(1,1)*ST_T2, VS1(1,2)*TT_T2, VS1(1,3)*DT_T2,
     & VS1(1,4)*UT_T2, VS1(1,5)*XT_T2,
     & BT2(1,2))
      CALL TRACE_TRANSITION_INTERVAL_BT2_TERMS('TRDIF',
     & TRACE_SIDE, TRACE_STATION,
     & 1, 3,
     & VS2(1,3),
     & VS1(1,1)*ST_D2, VS1(1,2)*TT_D2, VS1(1,3)*DT_D2,
     & VS1(1,4)*UT_D2, VS1(1,5)*XT_D2,
     & BT2(1,3))
      CALL TRACE_TRANSITION_INTERVAL_BT2_TERMS('TRDIF',
     & TRACE_SIDE, TRACE_STATION,
     & 1, 4,
     & VS2(1,4),
     & VS1(1,1)*ST_U2, VS1(1,2)*TT_U2, VS1(1,3)*DT_U2,
     & VS1(1,4)*UT_U2, VS1(1,5)*XT_U2,
     & BT2(1,4))
      CALL TRACE_TRANSITION_INTERVAL_BT2_TERMS('TRDIF',
     & TRACE_SIDE, TRACE_STATION,
     & 2, 2,
     & VS2(2,2),
     & VS1(2,1)*ST_T2, VS1(2,2)*TT_T2, VS1(2,3)*DT_T2,
     & VS1(2,4)*UT_T2, VS1(2,5)*XT_T2,
     & BT2(2,2))
      CALL TRACE_TRANSITION_INTERVAL_BT2_TERMS('TRDIF',
     & TRACE_SIDE, TRACE_STATION,
     & 2, 3,
     & VS2(2,3),
     & VS1(2,1)*ST_D2, VS1(2,2)*TT_D2, VS1(2,3)*DT_D2,
     & VS1(2,4)*UT_D2, VS1(2,5)*XT_D2,
     & BT2(2,3))
      CALL TRACE_TRANSITION_INTERVAL_BT2_TERMS('TRDIF',
     & TRACE_SIDE, TRACE_STATION,
     & 2, 4,
     & VS2(2,4),
     & VS1(2,1)*ST_U2, VS1(2,2)*TT_U2, VS1(2,3)*DT_U2,
     & VS1(2,4)*UT_U2, VS1(2,5)*XT_U2,
     & BT2(2,4))
      CALL TRACE_TRANSITION_INTERVAL_BT2_TERMS('TRDIF',
     & TRACE_SIDE, TRACE_STATION,
     & 3, 2,
     & VS2(3,2),
     & VS1(3,1)*ST_T2, VS1(3,2)*TT_T2, VS1(3,3)*DT_T2,
     & VS1(3,4)*UT_T2, VS1(3,5)*XT_T2,
     & BT2(3,2))
      CALL TRACE_TRANSITION_INTERVAL_BT2_TERMS('TRDIF',
     & TRACE_SIDE, TRACE_STATION,
     & 3, 3,
     & VS2(3,3),
     & VS1(3,1)*ST_D2, VS1(3,2)*TT_D2, VS1(3,3)*DT_D2,
     & VS1(3,4)*UT_D2, VS1(3,5)*XT_D2,
     & BT2(3,3))
      CALL TRACE_TRANSITION_INTERVAL_BT2_TERMS('TRDIF',
     & TRACE_SIDE, TRACE_STATION,
     & 3, 4,
     & VS2(3,4),
     & VS1(3,1)*ST_U2, VS1(3,2)*TT_U2, VS1(3,3)*DT_U2,
     & VS1(3,4)*UT_U2, VS1(3,5)*XT_U2,
     & BT2(3,4))
C
C---- Trace BL1/BT1 components at station 5 side 2
      IF(TRACE_SIDE.EQ.2 .AND. TRACE_STATION.EQ.5) THEN
       WRITE(0,'(A,Z8,A,Z8,A,Z8)')
     &  'F_BLBT21 BL1=',TRANSFER(BL1(2,1),1),
     &  ' BT1=',TRANSFER(BT1(2,1),1),
     &  ' SUM=',TRANSFER(BL1(2,1)+BT1(2,1),1)
C---- BL1(2,1) individual terms
       WRITE(0,'(A,Z8,A,Z8,A,Z8,A,Z8,A,Z8)')
     &  'F_BL121 v21=',TRANSFER(BL1VS121,1),
     &  ' p1=',TRANSFER(BL1P1,1),
     &  ' p2=',TRANSFER(BL1P2,1),
     &  ' p3=',TRANSFER(BL1P3,1),
     &  ' p4=',TRANSFER(BL1P4,1)
       WRITE(0,'(A,Z8,A,Z8,A,Z8)')
     &  'F_P2DBG VS23=',TRANSFER(VS2(2,3),1),
     &  ' DTA1=',TRANSFER(DT_A1,1),
     &  ' prod=',TRANSFER(VS2(2,3)*DT_A1,1)
      ENDIF
C---- Add up laminar and turbulent parts to get final system
C-    in terms of honest-to-God "1" and "2" variables.
      VSREZ(1) =            BTREZ(1)
      VSREZ(2) = BLREZ(2) + BTREZ(2)
      VSREZ(3) = BLREZ(3) + BTREZ(3)
      IF(TRACE_OUTER.EQ.33) THEN
        WRITE(0,'(A,I3,A,I3,6(1X,A,Z8.8))') 'F_TRDIF_OUT s=',TRACE_SIDE,
     &   ' ibl=',TRACE_STATION,
     &   'BLR1=',TRANSFER(BLREZ(1),1),
     &   'BLR2=',TRANSFER(BLREZ(2),1),
     &   'BLR3=',TRANSFER(BLREZ(3),1),
     &   'BTR1=',TRANSFER(BTREZ(1),1),
     &   'BTR2=',TRANSFER(BTREZ(2),1),
     &   'BTR3=',TRANSFER(BTREZ(3),1)
      ENDIF
      IF(TRACE_SIDE.EQ.2 .AND. TRACE_STATION.EQ.66
     &   .AND. TRACE_ITER.EQ.2) THEN
       WRITE(0,'(A,6(A,Z8))') 'F_TRDIF_HALVES_66 it=2',
     &  ' LAM_R0=',TRANSFER(BLREZ(1),1),
     &  ' LAM_R1=',TRANSFER(BLREZ(2),1),
     &  ' LAM_R2=',TRANSFER(BLREZ(3),1),
     &  ' TRB_R0=',TRANSFER(BTREZ(1),1),
     &  ' TRB_R1=',TRANSFER(BTREZ(2),1),
     &  ' TRB_R2=',TRANSFER(BTREZ(3),1)
      ENDIF
      IF(TRACE_SIDE.EQ.2 .AND. TRACE_STATION.EQ.5) THEN
        WRITE(0,'(A,I1,A,Z8,A,Z8,A,Z8,A,Z8,A,Z8,A,Z8)')
     &   'F_TR5 P=',TRACE_PHASE,
     &   ' BLR3=',TRANSFER(BLREZ(3),1),
     &   ' BTR3=',TRANSFER(BTREZ(3),1),
     &   ' R3=',TRANSFER(VSREZ(3),1),
     &   ' XT=',TRANSFER(XT,1),
     &   ' WF1=',TRANSFER(WF1,1),
     &   ' WF2=',TRANSFER(WF2,1)
      ENDIF
      IF(TRACE_SIDE.EQ.2 .AND. TRACE_STATION.EQ.50
     &   .AND. TRACE_ITER.EQ.1) THEN
        WRITE(0,'(A,Z8,A,Z8,A,Z8,A,Z8,A,Z8)')
     &   'F_TRDIF_COMB R1=',TRANSFER(VSREZ(1),1),
     &   ' R2=',TRANSFER(VSREZ(2),1),
     &   ' R3=',TRANSFER(VSREZ(3),1),
     &   ' BL2=',TRANSFER(BLREZ(2),1),
     &   ' BT2=',TRANSFER(BTREZ(2),1)
      ENDIF
      VSM(1)   =            BTM(1)
      VSM(2)   = BLM(2)   + BTM(2)
      VSM(3)   = BLM(3)   + BTM(3)
C---- n6h20 trace: residual+Jacobian at IBL=66 IS=2 MRCHDU 10 iter 1-2
      IF(TRACE_SIDE.EQ.2 .AND. TRACE_STATION.EQ.66
     &   .AND. (TRACE_ITER.EQ.1 .OR. TRACE_ITER.EQ.2)) THEN
        WRITE(0,'(A,I2,3(A,Z8))')
     &   'F_MDU10_N66_RHS it=',TRACE_ITER,
     &   ' R0=',TRANSFER(VSREZ(1),1),
     &   ' R1=',TRANSFER(VSREZ(2),1),
     &   ' R2=',TRANSFER(VSREZ(3),1)
        WRITE(0,'(A,I2,A,4(A,Z8))')
     &   'F_MDU10_N66_VS2 it=',TRACE_ITER,' row=0',
     &   ' c0=',TRANSFER(VS2(1,1),1),
     &   ' c1=',TRANSFER(VS2(1,2),1),
     &   ' c2=',TRANSFER(VS2(1,3),1),
     &   ' c3=',TRANSFER(VS2(1,4),1)
        WRITE(0,'(A,I2,A,4(A,Z8))')
     &   'F_MDU10_N66_VS2 it=',TRACE_ITER,' row=1',
     &   ' c0=',TRANSFER(VS2(2,1),1),
     &   ' c1=',TRANSFER(VS2(2,2),1),
     &   ' c2=',TRANSFER(VS2(2,3),1),
     &   ' c3=',TRANSFER(VS2(2,4),1)
        WRITE(0,'(A,I2,A,4(A,Z8))')
     &   'F_MDU10_N66_VS2 it=',TRACE_ITER,' row=2',
     &   ' c0=',TRANSFER(VS2(3,1),1),
     &   ' c1=',TRANSFER(VS2(3,2),1),
     &   ' c2=',TRANSFER(VS2(3,3),1),
     &   ' c3=',TRANSFER(VS2(3,4),1)
      ENDIF
      VSR(1)   =            BTR(1)
      VSR(2)   = BLR(2)   + BTR(2)
      VSR(3)   = BLR(3)   + BTR(3)
      VSX(1)   =            BTX(1)
      VSX(2)   = BLX(2)   + BTX(2)
      VSX(3)   = BLX(3)   + BTX(3)
C---- VSX row 3 trace for case 188 NACA 0009 a=-2 Nc=12
      IF(TRACE_SIDE.EQ.1 .AND. TRACE_STATION.EQ.78) THEN
        WRITE(0,'(A,A,Z8,A,Z8,A,Z8)')
     &   'F_VSX78R3F',
     &   ' BLX=',TRANSFER(BLX(3),1),
     &   ' BTX=',TRANSFER(BTX(3),1),
     &   ' VSX=',TRANSFER(VSX(3),1)
      ENDIF
      IF(TRACE_SIDE.EQ.1 .AND. TRACE_STATION.EQ.79) THEN
       WRITE(*,'(A,Z8,A,Z8,A,Z8,A,Z8,A,Z8,A,Z8)')
     &  'F_TRDIF_VSX BTX1=',TRANSFER(BTX(1),1),
     &  ' BLX2=',TRANSFER(BLX(2),1),
     &  ' BTX2=',TRANSFER(BTX(2),1),
     &  ' BLX3=',TRANSFER(BLX(3),1),
     &  ' BTX3=',TRANSFER(BTX(3),1),
     &  ' VSX1=',TRANSFER(VSX(1),1)
      ENDIF
      DO 60 L=1, 5
        VS1(1,L) =            BT1(1,L)
        VS2(1,L) =            BT2(1,L)
        VS1(2,L) = BL1(2,L) + BT1(2,L)
        VS2(2,L) = BL2(2,L) + BT2(2,L)
        VS1(3,L) = BL1(3,L) + BT1(3,L)
        VS2(3,L) = BL2(3,L) + BT2(3,L)
   60 CONTINUE
C---- n6h20 focused row-0 VS1/VS2 dump at IBL=66 IS=2 MRCHDU 10 iter 2
      IF(TRACE_SIDE.EQ.2 .AND. TRACE_STATION.EQ.66
     &   .AND. TRACE_OUTER.EQ.10 .AND. TRACE_ITER.EQ.2) THEN
        WRITE(0,'(A,10(1X,A,Z8.8))')
     &    'F_TRDIF_R0_66',
     &    'V11=',TRANSFER(REAL(VS1(1,1)),1),
     &    'V12=',TRANSFER(REAL(VS1(1,2)),1),
     &    'V13=',TRANSFER(REAL(VS1(1,3)),1),
     &    'V14=',TRANSFER(REAL(VS1(1,4)),1),
     &    'V15=',TRANSFER(REAL(VS1(1,5)),1),
     &    'V21=',TRANSFER(REAL(VS2(1,1)),1),
     &    'V22=',TRANSFER(REAL(VS2(1,2)),1),
     &    'V23=',TRANSFER(REAL(VS2(1,3)),1),
     &    'V24=',TRANSFER(REAL(VS2(1,4)),1),
     &    'V25=',TRANSFER(REAL(VS2(1,5)),1)
      ENDIF
C---- Final combined VS1/VS2 row 3 at station 78 side 1 Newton outer iter 5
      IF(TRACE_SIDE.EQ.1 .AND. TRACE_STATION.EQ.78
     &   .AND. TRACE_OUTER.EQ.5) THEN
        WRITE(0,'(A,I3,11(1X,A,Z8.8))')
     &    'F_VS3FINAL_78 it=',TRACE_OUTER,
     &    'VS1_0=',TRANSFER(REAL(VS1(3,1)),1),
     &    'VS1_1=',TRANSFER(REAL(VS1(3,2)),1),
     &    'VS1_2=',TRANSFER(REAL(VS1(3,3)),1),
     &    'VS1_3=',TRANSFER(REAL(VS1(3,4)),1),
     &    'VS1_4=',TRANSFER(REAL(VS1(3,5)),1),
     &    'VS2_0=',TRANSFER(REAL(VS2(3,1)),1),
     &    'VS2_1=',TRANSFER(REAL(VS2(3,2)),1),
     &    'VS2_2=',TRANSFER(REAL(VS2(3,3)),1),
     &    'VS2_3=',TRANSFER(REAL(VS2(3,4)),1),
     &    'VS2_4=',TRANSFER(REAL(VS2(3,5)),1),
     &    'VSX=',TRANSFER(REAL(VSX(3)),1)
      ENDIF
C---- Station 78 side 1 outer iter 3: full BL2/BT2 trace for 1 ULP diff
      IF(TRACE_SIDE.EQ.1 .AND. TRACE_STATION.EQ.78
     &   .AND. TRACE_OUTER.EQ.3) THEN
        DO 9881 KKK=1, 3
          DO 9882 LLL=1, 5
            WRITE(0,'(A,I1,A,I1,4(A,Z8))')
     &       'F_BLBT78_3 r',KKK-1,' L',LLL-1,
     &       ' BL1=',TRANSFER(REAL(BL1(KKK,LLL)),1),
     &       ' BT1=',TRANSFER(REAL(BT1(KKK,LLL)),1),
     &       ' BL2=',TRANSFER(REAL(BL2(KKK,LLL)),1),
     &       ' BT2=',TRANSFER(REAL(BT2(KKK,LLL)),1)
 9882     CONTINUE
 9881   CONTINUE
      ENDIF
C---- TRDIF row 3 (energy) BL/BT trace for case 188 NACA 0009 at Newton outer iter 5
      IF(TRACE_SIDE.EQ.1 .AND. TRACE_STATION.EQ.78
     &   .AND. TRACE_OUTER.EQ.5) THEN
        DO 9778 L=1, 5
          WRITE(0,'(A,I1,A,I3,A,I1,A,Z8,A,Z8,A,Z8,A,Z8)')
     &     'F_TRDIF_R3 s=',TRACE_SIDE,' i=',TRACE_STATION,
     &     ' L=',L,
     &     ' BL1=',TRANSFER(REAL(BL1(3,L)),1),
     &     ' BT1=',TRANSFER(REAL(BT1(3,L)),1),
     &     ' BL2=',TRANSFER(REAL(BL2(3,L)),1),
     &     ' BT2=',TRANSFER(REAL(BT2(3,L)),1)
 9778   CONTINUE
      ENDIF
C---- TRDIF rows 1+2 at iter 5 station 78 side 1 for case 188
      IF(TRACE_SIDE.EQ.1 .AND. TRACE_STATION.EQ.78
     &   .AND. TRACE_ITER.EQ.5) THEN
        DO 9779 L=1, 5
          WRITE(0,'(A,I1,A,Z8,A,Z8,A,Z8,A,Z8)')
     &     'F_TRDIF78_R1 L=',L,
     &     ' BL1=',TRANSFER(REAL(BL1(1,L)),1),
     &     ' BT1=',TRANSFER(REAL(BT1(1,L)),1),
     &     ' BL2=',TRANSFER(REAL(BL2(1,L)),1),
     &     ' BT2=',TRANSFER(REAL(BT2(1,L)),1)
 9779   CONTINUE
        DO 9780 L=1, 5
          WRITE(0,'(A,I1,A,Z8,A,Z8,A,Z8,A,Z8)')
     &     'F_TRDIF78_R2 L=',L,
     &     ' BL1=',TRANSFER(REAL(BL1(2,L)),1),
     &     ' BT1=',TRANSFER(REAL(BT1(2,L)),1),
     &     ' BL2=',TRANSFER(REAL(BL2(2,L)),1),
     &     ' BT2=',TRANSFER(REAL(BT2(2,L)),1)
 9780   CONTINUE
      ENDIF
      IF(TRACE_SIDE.EQ.2 .AND. TRACE_STATION.EQ.50) THEN
       WRITE(0,'(A,Z8,1X,Z8,1X,Z8)')
     &  'F_BLBT_COMBINE ',
     &  TRANSFER(BL1(2,3),1),TRANSFER(BT1(2,3),1),
     &  TRANSFER(VS1(2,3),1)
      ENDIF
C
C---- GDB: dump transition point interpolation at transition stations
      IF((TRACE_SIDE.EQ.2 .AND. TRACE_STATION.EQ.58)
     &  .OR.(TRACE_SIDE.EQ.1 .AND. TRACE_STATION.EQ.47)
     &   .AND. TRACE_ITER.EQ.1) THEN
        WRITE(0,'(A,Z8,A,Z8,A,Z8,A,Z8,A,Z8)')
     &   'F_XTPT xt=',TRANSFER(XT,1),
     &   ' tt=',TRANSFER(TT,1),
     &   ' dt=',TRANSFER(DT,1),
     &   ' ut=',TRANSFER(UT,1),
     &   ' st=',TRANSFER(ST,1)
      ENDIF
C---- GDB: dump BL2/BT2/VS2 at transition stations
      IF((TRACE_SIDE.EQ.2 .AND. TRACE_STATION.EQ.58)
     &  .OR.(TRACE_SIDE.EQ.1 .AND. TRACE_STATION.EQ.47)) THEN
        WRITE(0,'(A,I2,A,Z8,A,Z8,A,Z8)')
     &   'F_BT21_58 it=',TRACE_ITER,
     &   ' bl2=',TRANSFER(BL2(2,2),1),
     &   ' bt2=',TRANSFER(BT2(2,2),1),
     &   ' vs2=',TRANSFER(VS2(2,2),1)
        WRITE(0,'(A,I2,A,Z8,A,Z8,A,Z8,A,Z8,'//
     &   'A,Z8,A,Z8)')
     &   'F_BT21T it=',TRACE_ITER,
     &   ' base=',TRANSFER(VS2(2,2),1),
     &   ' st=',TRANSFER(VS1(2,1)*ST_T2,1),
     &   ' tt=',TRANSFER(VS1(2,2)*TT_T2,1),
     &   ' dt=',TRANSFER(VS1(2,3)*DT_T2,1),
     &   ' ut=',TRANSFER(VS1(2,4)*UT_T2,1),
     &   ' xt=',TRANSFER(VS1(2,5)*XT_T2,1)
        WRITE(0,'(A,I2,A,Z8,A,Z8)')
     &   'F_BT21O it=',TRACE_ITER,
     &   ' vs121=',TRANSFER(VS1(2,1),1),
     &   ' st_t2=',TRANSFER(ST_T2,1)
      ENDIF
      CALL TRACE_TRANSITION_INTERVAL_FINAL_TERMS('TRDIF',
     & TRACE_SIDE, TRACE_STATION,
     & 2, 2,
     & BL2(2,2), BT2(2,2), VS2(2,2))
      CALL TRACE_TRANSITION_INTERVAL_FINAL_TERMS('TRDIF',
     & TRACE_SIDE, TRACE_STATION,
     & 2, 3,
     & BL2(2,3), BT2(2,3), VS2(2,3))
      CALL TRACE_TRANSITION_INTERVAL_FINAL_TERMS('TRDIF',
     & TRACE_SIDE, TRACE_STATION,
     & 2, 4,
     & BL2(2,4), BT2(2,4), VS2(2,4))
C
C---- trace the TRDIF interpolation/chain inputs before the final row sums
      CALL TRACE_TRANSITION_INTERVAL_TERM_COMPONENTS('TRDIF',
     & WF2, WF2_XT,
     & WF1_A1, WF1_T1, WF1_T2,
     & WF2_A1, WF2_T1, WF2_T2,
     & WF2_XT*XT_X1, (WF2-1.0)/(X2-X1ORIG),
     & WF2_XT*XT_X2, WF2/(X2-X1ORIG),
     & T1ORIG*WF1_A1, T2*WF2_A1,
     & D1ORIG*WF1_A1, D2*WF2_A1,
     & D1ORIG*WF1_T1, D2*WF2_T1,
     & D1ORIG*WF1_T2, D2*WF2_T2,
     & U1ORIG*WF1_A1, U2*WF2_A1,
     & U1ORIG*WF1_T1, U2*WF2_T1,
     & U1ORIG*WF1_T2, U2*WF2_T2)
C
      CALL TRACE_TRANSITION_INTERVAL_INPUTS('TRDIF',
     & X1, X2, XT,
     & X1ORIG, T1ORIG, D1ORIG, S2, U1ORIG,
     & T1, T2, D1, D2, U1, U2,
     & XT_A1, XT_T1, XT_T2, XT_D1, XT_D2,
     & XT_U1, XT_U2, XT_X1, XT_X2,
     & WF2_A1, WF2_T1, WF2_T2, WF2_D1, WF2_D2,
     & WF2_U1, WF2_U2, WF2_X1, WF2_X2,
     & TT_A1, TT_T1, TT_T2, TT_D1, TT_D2,
     & DT_A1, DT_T1, DT_T2, DT_D1, DT_D2,
     & UT_A1, UT_T1, UT_T2, UT_D1, UT_D2, UT_U1, UT_U2,
     & ST, ST_A1, ST_T1, ST_T2, ST_D1, ST_D2,
     & ST_U1, ST_U2, ST_X1, ST_X2)
C
C---- structured trace for parity debugging of the transition interval
      CALL TRACE_TRANSITION_INTERVAL_ROWS('TRDIF',
     & X1, X2, XT, WF1, WF2, TT, DT, UT, ST,
     & BLREZ(2), BLREZ(3),
     & BL1(3,1), BL1(3,2), BL1(3,3), BL1(3,4), BL1(3,5),
     & BL2(3,1), BL2(3,2), BL2(3,3), BL2(3,4), BL2(3,5),
     & BL2(2,2), BL2(1,4), BL2(2,4),
     & BTREZ(1), BTREZ(2), BTREZ(3),
     & BT1(3,1), BT1(3,2), BT1(3,3), BT1(3,4), BT1(3,5),
     & BT2(3,1), BT2(3,2), BT2(3,3), BT2(3,4), BT2(3,5),
     & BT2(2,2), BT2(1,4), BT2(2,4),
     & VS2(2,2), VS2(1,4), VS2(2,4),
     & VSREZ(3), VS2(3,1), VS2(3,2), VS2(3,3), VS2(3,4), VS2(3,5))
C
C---- To be sanitary, restore "1" quantities which got clobbered
C-    in all of the numerical gymnastics above.  The "2" variables
C-    were already restored for the XT-X2 differencing part.
      DO 70 ICOM=1, NCOM
        COM1(ICOM) = C1SAV(ICOM)
   70 CONTINUE
C
      RETURN
      END
 
 
      SUBROUTINE BLDIF(ITYP)
C-----------------------------------------------------------
C     Sets up the Newton system coefficients and residuals
C
C        ITYP = 0 :  similarity station
C        ITYP = 1 :  laminar interval
C        ITYP = 2 :  turbulent interval
C        ITYP = 3 :  wake interval
C
C     This routine knows nothing about a transition interval,
C     which is taken care of by TRDIF.
C-----------------------------------------------------------
      IMPLICIT REAL(M)
      INCLUDE 'XBL.INC'
C
      CALL TRACE_ENTER('BLDIF')
C
      IF(TRACE_SIDE.EQ.2 .AND. TRACE_STATION.EQ.98) THEN
       WRITE(0,'(A,5(A,Z8))')
     &  'F_COM1_98',
     &  ' HK1=',TRANSFER(HK1,1),
     &  ' HS1=',TRANSFER(HS1,1),
     &  ' DI1=',TRANSFER(DI1,1),
     &  ' US1=',TRANSFER(US1,1),
     &  ' RT1=',TRANSFER(RT1,1)
      ENDIF
      IF(TRACE_SIDE.EQ.2 .AND. TRACE_STATION.EQ.71) THEN
       WRITE(*,'(A,I1,A,I3,A,I1)')
     &  'F_BLDIF_ENTRY s=',TRACE_SIDE,' i=',TRACE_STATION,
     &  ' ty=',ITYP
      ENDIF
C
      IF(ITYP.EQ.0) THEN
C----- similarity logarithmic differences  (prescribed)
       XLOG = 1.0
       ULOG = BULE
       TLOG = 0.5*(1.0 - BULE)
       HLOG = 0.
       DDLOG = 0.
C----- s1210 dump closure at simi
       IF(TRACE_SIDE.EQ.1 .AND. TRACE_STATION.EQ.2) THEN
        WRITE(0,'(A,8(A,Z8))') 'F_S1210_CLO',
     &   ' HS2=',TRANSFER(HS2,1),
     &   ' HS2T=',TRANSFER(HS2_T2,1),
     &   ' HS2D=',TRANSFER(HS2_D2,1),
     &   ' HS2U=',TRANSFER(HS2_U2,1),
     &   ' CF2=',TRANSFER(CF2,1),
     &   ' CF2T=',TRANSFER(CF2_T2,1),
     &   ' CF2D=',TRANSFER(CF2_D2,1),
     &   ' CF2U=',TRANSFER(CF2_U2,1)
        WRITE(0,'(A,8(A,Z8))') 'F_S1210_CL2',
     &   ' DI2=',TRANSFER(DI2,1),
     &   ' DI2T=',TRANSFER(DI2_T2,1),
     &   ' DI2D=',TRANSFER(DI2_D2,1),
     &   ' DI2U=',TRANSFER(DI2_U2,1),
     &   ' US2=',TRANSFER(US2,1),
     &   ' US2T=',TRANSFER(US2_T2,1),
     &   ' US2D=',TRANSFER(US2_D2,1),
     &   ' US2U=',TRANSFER(US2_U2,1)
        WRITE(0,'(A,6(A,Z8))') 'F_S1210_KIN',
     &   ' HK2=',TRANSFER(HK2,1),
     &   ' HK2T=',TRANSFER(HK2_T2,1),
     &   ' HK2D=',TRANSFER(HK2_D2,1),
     &   ' HK2U=',TRANSFER(HK2_U2,1),
     &   ' RT2T=',TRANSFER(RT2_T2,1),
     &   ' RT2U=',TRANSFER(RT2_U2,1)
       ENDIF
      ELSE
C----- usual logarithmic differences
       XLOG = LOG(X2/X1)
       ULOG = LOG(U2/U1)
       TLOG = LOG(T2/T1)
       HLOG = LOG(HS2/HS1)
C       XLOG = 2.0*(X2-X1)/(X2+X1)
C       ULOG = 2.0*(U2-U1)/(U2+U1)
C       TLOG = 2.0*(T2-T1)/(T2+T1)
C       HLOG = 2.0*(HS2-HS1)/(HS2+HS1)
       DDLOG = 1.0
      ENDIF
C
      CALL TRACE_BLDIF_LOG_INPUTS('BLDIF',
     &     TRACE_SIDE, TRACE_STATION, TRACE_PHASE, ITYP,
     &     X1, X2, U1, U2, T1, T2, HS1, HS2,
     &     X2/X1, U2/U1, T2/T1, HS2/HS1)
C
C---- HEX TRACE: BLDIF LOG terms for stn 3-4 side 1 MRCHDU
      IF(TRACE_SIDE.EQ.1
     &   .AND. (TRACE_STATION.EQ.3
     &          .OR.TRACE_STATION.EQ.4)
     &   .AND. TRACE_PHASE.EQ.3) THEN
        WRITE(0,'(A,I2,A,I2,A,Z8,A,Z8,'//
     &   'A,Z8,A,Z8)')
     &   'F_STN',TRACE_STATION,
     &   '_LOG   iter=',TRACE_ITER,
     &   ' xlog=',TRANSFER(XLOG,1),
     &   ' ulog=',TRANSFER(ULOG,1),
     &   ' tlog=',TRANSFER(TLOG,1),
     &   ' hlog=',TRANSFER(HLOG,1)
      ENDIF
C
      DO 55 K=1, 4
        VSREZ(K) = 0.
        VSM(K) = 0.
        VSR(K) = 0.
        VSX(K) = 0.
        DO 551 L=1, 5
          VS1(K,L) = 0.
          VS2(K,L) = 0.
  551   CONTINUE
   55 CONTINUE
C
C---- set triggering constant for local upwinding
      HUPWT = 1.0
C
ccc      HDCON = 5.0*HUPWT
ccc      HD_HK1 = 0.0
ccc      HD_HK2 = 0.0
C
      HDCON  =  5.0*HUPWT/HK2**2
      HD_HK1 =  0.0
      HD_HK2 = -HDCON*2.0/HK2
C
C---- use less upwinding in the wake
      IF(ITYP.EQ.3) THEN
       HDCON  =  HUPWT/HK2**2
       HD_HK1 =  0.0
       HD_HK2 = -HDCON*2.0/HK2
      ENDIF
C
C---- local upwinding is based on local change in  log(Hk-1)
C-    (mainly kicks in at transition)
      ARG = ABS((HK2-1.0)/(HK1-1.0))
      HL = LOG(ARG)
      HL_HK1 = -1.0/(HK1-1.0)
      HL_HK2 =  1.0/(HK2-1.0)
C
C---- set local upwinding parameter UPW and linearize it
C
C       UPW = 0.5   Trapezoidal
C       UPW = 1.0   Backward Euler
C
      HLSQ = MIN( HL**2 , 15.0 )
      EHH = EXP(-HLSQ*HDCON)
      UPW = 1.0 - 0.5*EHH
      UPW_HL =        EHH * HL  *HDCON
      UPW_HD =    0.5*EHH * HLSQ
C
      UPW_HK1 = UPW_HL*HL_HK1 + UPW_HD*HD_HK1
      UPW_HK2 = UPW_HL*HL_HK2 + UPW_HD*HD_HK2
C
      UPW_U1 = UPW_HK1*HK1_U1
      UPW_T1 = UPW_HK1*HK1_T1
      UPW_D1 = UPW_HK1*HK1_D1
      UPW_U2 = UPW_HK2*HK2_U2
      UPW_T2 = UPW_HK2*HK2_T2
      UPW_D2 = UPW_HK2*HK2_D2
      UPW_MS = UPW_HK1*HK1_MS
     &       + UPW_HK2*HK2_MS
C
C---- trace the BLDIF state before equation assembly
      IF(ABS(HK1_D1).GT.0.0) THEN
       HSHK1TR = HS1_D1/HK1_D1
      ELSE
       HSHK1TR = 0.
      ENDIF
      IF(ABS(HK2_D2).GT.0.0) THEN
       HSHK2TR = HS2_D2/HK2_D2
      ELSE
       HSHK2TR = 0.
      ENDIF
      CALL TRACE_BLDIF_PRIMARY_STATION('BLDIF', ITYP, 1,
     &     X1, U1, T1, D1, S1, M1, H1, HK1, RT1)
      CALL TRACE_BLDIF_PRIMARY_STATION('BLDIF', ITYP, 2,
     &     X2, U2, T2, D2, S2, M2, H2, HK2, RT2)
      CALL TRACE_BLDIF_SECONDARY_STATION('BLDIF', ITYP, 1,
     &     HC1, HS1, HSHK1TR, HK1_D1, HS1_D1, HS1_T1,
     &     US1, US1_T1, HK1_U1, RT1_T1, RT1_U1,
     &     CQ1, CF1, CF1_U1, CF1_T1, CF1_D1, CF1_MS,
     &     CFM_U1, CFM_T1, CFM_D1, CFM_MS,
     &     DI1, DI1_T1, DE1)
      CALL TRACE_BLDIF_SECONDARY_STATION('BLDIF', ITYP, 2,
     &     HC2, HS2, HSHK2TR, HK2_D2, HS2_D2, HS2_T2,
     &     US2, US2_T2, HK2_U2, RT2_T2, RT2_U2,
     &     CQ2, CF2, CF2_U2, CF2_T2, CF2_D2, CF2_MS,
     &     CFM_U2, CFM_T2, CFM_D2, CFM_MS,
     &     DI2, DI2_T2, DE2)
      IF(TRACE_SIDE.EQ.2 .AND. (TRACE_STATION.EQ.85
     &   .OR. TRACE_STATION.EQ.86) .AND. TRACE_ITER.EQ.1) THEN
        WRITE(0,'(A,I3,8(1X,Z8.8))')
     &   'F_COM1_85 I=',TRACE_STATION,
     &   TRANSFER(HK1,1), TRANSFER(RT1,1),
     &   TRANSFER(CF1,1), TRANSFER(DI1,1),
     &   TRANSFER(HS1,1), TRANSFER(US1,1),
     &   TRANSFER(CQ1,1), TRANSFER(DE1,1)
        WRITE(0,'(A,I3,8(1X,Z8.8))')
     &   'F_COM2_85 I=',TRACE_STATION,
     &   TRANSFER(HK2,1), TRANSFER(RT2,1),
     &   TRANSFER(CF2,1), TRANSFER(DI2,1),
     &   TRANSFER(HS2,1), TRANSFER(US2,1),
     &   TRANSFER(CQ2,1), TRANSFER(DE2,1)
      ENDIF
      CALL TRACE_BLDIF_COMMON('BLDIF', ITYP, CFM, UPW,
     &     XLOG, ULOG, TLOG, HLOG, DDLOG)
      CALL TRACE_BLDIF_UPW_TERMS('BLDIF', ITYP,
     &     HK1, HK2,
     &     HK1_T1, HK1_D1, HK1_U1, HK1_MS,
     &     HK2_T2, HK2_D2, HK2_U2, HK2_MS,
     &     HL, HLSQ, EHH, UPW_HL, UPW_HD, UPW_HK1, UPW_HK2,
     &     UPW_T1, UPW_D1, UPW_U1, UPW_T2, UPW_D2, UPW_U2,
     &     UPW_MS)
C
C
      IF(ITYP.EQ.0) THEN
C
C***** LE point -->  set zero amplification factor
       VS2(1,1) = 1.0
       VSR(1)   = 0.
       VSREZ(1) = -AMPL2
C
      ELSE IF(ITYP.EQ.1) THEN
C
C***** laminar part -->  set amplification equation
C
C----- set average amplification AX over interval X1..X2
       CALL AXSET( HK1,    T1,    RT1, AMPL1,  
     &             HK2,    T2,    RT2, AMPL2, AMCRIT, IDAMPV,
     &      AX, AX_HK1, AX_T1, AX_RT1, AX_A1,
     &          AX_HK2, AX_T2, AX_RT2, AX_A2 )
C
       REZC = AMPL2 - AMPL1 - AX*(X2-X1)
       Z_AX = -(X2-X1)
C
       VS1(1,1) = Z_AX* AX_A1  -  1.0
       VS1(1,2) = Z_AX*(AX_HK1*HK1_T1 + AX_T1 + AX_RT1*RT1_T1)
       VS1(1,3) = Z_AX*(AX_HK1*HK1_D1                        )
       VS1(1,4) = Z_AX*(AX_HK1*HK1_U1         + AX_RT1*RT1_U1)
       VS1(1,5) =  AX
       VS2(1,1) = Z_AX* AX_A2  +  1.0
       VS2(1,2) = Z_AX*(AX_HK2*HK2_T2 + AX_T2 + AX_RT2*RT2_T2)
       VS2(1,3) = Z_AX*(AX_HK2*HK2_D2                        )
C---- GDB: dump laminar VS2(1,3) operands at station 58
       IF(TRACE_SIDE.EQ.1 .AND. TRACE_STATION.EQ.97) THEN
         WRITE(0,'(A,Z8,A,Z8,A,Z8,A,Z8)')
     &    'F_AXHK zax=',TRANSFER(Z_AX,1),
     &    ' axhk=',TRANSFER(AX_HK2,1),
     &    ' hkd=',TRANSFER(HK2_D2,1),
     &    ' res=',TRANSFER(VS2(1,3),1)
       ENDIF
       VS2(1,4) = Z_AX*(AX_HK2*HK2_U2         + AX_RT2*RT2_U2)
       VS2(1,5) = -AX
       VSM(1)   = Z_AX*(AX_HK1*HK1_MS         + AX_RT1*RT1_MS
     &                + AX_HK2*HK2_MS         + AX_RT2*RT2_MS)
       VSR(1)   = Z_AX*(                        AX_RT1*RT1_RE
     &                                        + AX_RT2*RT2_RE)
       VSX(1)   = 0.
       VSREZ(1) = -REZC
       IF(TRACE_SIDE.EQ.2
     &    .AND. TRACE_STATION.GE.2
     &    .AND. TRACE_STATION.LE.4
     &    .AND. TRACE_PHASE.EQ.1) THEN
        WRITE(0,'(A,I2,A,I3,A,Z8,A,Z8,A,Z8,'//
     &   'A,Z8,A,Z8,A,Z8,A,Z8,A,Z8,A,Z8,A,Z8)')
     &   'F_AXDER IS=',TRACE_SIDE,' IBL=',TRACE_STATION,
     &   ' HK1T=',TRANSFER(HK1_T1,1),
     &   ' HK1D=',TRANSFER(HK1_D1,1),
     &   ' HK1U=',TRANSFER(HK1_U1,1),
     &   ' RT1T=',TRANSFER(RT1_T1,1),
     &   ' RT1U=',TRANSFER(RT1_U1,1),
     &   ' HK2T=',TRANSFER(HK2_T2,1),
     &   ' HK2D=',TRANSFER(HK2_D2,1),
     &   ' HK2U=',TRANSFER(HK2_U2,1),
     &   ' RT2T=',TRANSFER(RT2_T2,1),
     &   ' RT2U=',TRANSFER(RT2_U2,1)
        WRITE(0,'(A,I2,A,I3,A,Z8,A,Z8,A,Z8,'//
     &   'A,Z8,A,Z8,A,Z8,A,Z8,A,Z8,A,Z8,A,Z8,'//
     &   'A,Z8,A,Z8,A,Z8)')
     &   'F_AXROW IS=',TRACE_SIDE,' IBL=',TRACE_STATION,
     &   ' ZAX=',TRANSFER(Z_AX,1),
     &   ' REZC=',TRANSFER(REZC,1),
     &   ' R1=',TRANSFER(VSREZ(1),1),
     &   ' V11=',TRANSFER(VS1(1,1),1),
     &   ' V12=',TRANSFER(VS1(1,2),1),
     &   ' V13=',TRANSFER(VS1(1,3),1),
     &   ' V14=',TRANSFER(VS1(1,4),1),
     &   ' V15=',TRANSFER(VS1(1,5),1),
     &   ' V21=',TRANSFER(VS2(1,1),1),
     &   ' V22=',TRANSFER(VS2(1,2),1),
     &   ' V23=',TRANSFER(VS2(1,3),1),
     &   ' V24=',TRANSFER(VS2(1,4),1),
     &   ' V25=',TRANSFER(VS2(1,5),1)
       ENDIF
       CALL TRACE_BLDIF_LAMINAR_AX_TERMS('BLDIF',
     &      X1, X2, T1, T2, D1, D2, HK1, HK2, RT1, RT2, AMPL1, AMPL2,
     &      Z_AX, AX, AX_HK1, AX_T1, AX_RT1, AX_A1,
     &      AX_HK2, AX_T2, AX_RT2, AX_A2,
     &      AX_HK1*HK1_T1, AX_T1, AX_RT1*RT1_T1,
     &      AX_HK1*HK1_T1 + AX_T1 + AX_RT1*RT1_T1, AX_HK1*HK1_D1,
     &      AX_HK2*HK2_T2, AX_T2, AX_RT2*RT2_T2,
     &      AX_HK2*HK2_T2 + AX_T2 + AX_RT2*RT2_T2, AX_HK2*HK2_D2)
C
      ELSE
C
C***** turbulent part -->  set shear lag equation
C
       SA  = (1.0-UPW)*S1  + UPW*S2
       CQA = (1.0-UPW)*CQ1 + UPW*CQ2
       CFA = (1.0-UPW)*CF1 + UPW*CF2
       HKA = (1.0-UPW)*HK1 + UPW*HK2
C
       USA = 0.5*(US1 + US2)
       RTA = 0.5*(RT1 + RT2)
       DEA = 0.5*(DE1 + DE2)
       IF(TRACE_SIDE.EQ.2 .AND. (TRACE_STATION.EQ.85
     &    .OR.TRACE_STATION.EQ.86) .AND.TRACE_ITER.EQ.1) THEN
        WRITE(0,'(A,I3,A,Z8,A,Z8,A,Z8,A,Z8)')
     &   'F_DEA I=',TRACE_STATION,
     &   ' de1=',TRANSFER(DE1,1),
     &   ' de2=',TRANSFER(DE2,1),
     &   ' dea=',TRANSFER(DEA,1),
     &   ' u2=',TRANSFER(U2,1)
       ENDIF
       DA  = 0.5*(D1  + D2 )
C
C
       IF(ITYP.EQ.3) THEN
C------ increased dissipation length in wake (decrease its reciprocal)
        ALD = DLCON
       ELSE
        ALD = 1.0
       ENDIF
C
C----- set and linearize  equilibrium 1/Ue dUe/dx   ...  NEW  12 Oct 94
       IF(ITYP.EQ.2) THEN
        GCC = GCCON
        HKC     = HKA - 1.0 - GCC/RTA
        HKC_HKA = 1.0
        HKC_RTA =             GCC/RTA**2
        IF(HKC .LT. 0.01) THEN
         HKC = 0.01
         HKC_HKA = 0.0
         HKC_RTA = 0.0
        ENDIF
       ELSE
        GCC = 0.0
        HKC = HKA - 1.0
        HKC_HKA = 1.0
        HKC_RTA = 0.0
       ENDIF
C
       HR     = HKC     / (GACON*ALD*HKA)
       HR_HKA = HKC_HKA / (GACON*ALD*HKA) - HR / HKA
       HR_RTA = HKC_RTA / (GACON*ALD*HKA)
C
       UQ     = (0.5*CFA - HR**2) / (GBCON*DA)
       UQ_HKA =   -2.0*HR*HR_HKA  / (GBCON*DA)
       UQ_RTA =   -2.0*HR*HR_RTA  / (GBCON*DA)
       UQ_CFA =   0.5             / (GBCON*DA)
       UQ_DA  = -UQ/DA
       UQ_UPW = UQ_CFA*(CF2-CF1) + UQ_HKA*(HK2-HK1)
       CALL TRACE_BLDIF_EQ1_UQ_TERMS('BLDIF', ITYP,
     & CFA, HKA, RTA, DA, ALD, HKC, HKC_HKA, HKC_RTA,
     & HR, HR_HKA, HR_RTA, UQ, UQ_HKA, UQ_RTA, UQ_CFA, UQ_DA)
C
       UQ_T1 = (1.0-UPW)*(UQ_CFA*CF1_T1 + UQ_HKA*HK1_T1) + UQ_UPW*UPW_T1
       UQ_D1 = (1.0-UPW)*(UQ_CFA*CF1_D1 + UQ_HKA*HK1_D1) + UQ_UPW*UPW_D1
       UQ_U1 = (1.0-UPW)*(UQ_CFA*CF1_U1 + UQ_HKA*HK1_U1) + UQ_UPW*UPW_U1
       UQ_T2 =      UPW *(UQ_CFA*CF2_T2 + UQ_HKA*HK2_T2) + UQ_UPW*UPW_T2
       UQ_D2 =      UPW *(UQ_CFA*CF2_D2 + UQ_HKA*HK2_D2) + UQ_UPW*UPW_D2
       UQ_U2 =      UPW *(UQ_CFA*CF2_U2 + UQ_HKA*HK2_U2) + UQ_UPW*UPW_U2
       UQ_MS = (1.0-UPW)*(UQ_CFA*CF1_MS + UQ_HKA*HK1_MS) + UQ_UPW*UPW_MS
     &       +      UPW *(UQ_CFA*CF2_MS + UQ_HKA*HK2_MS)
       UQ_RE = (1.0-UPW)* UQ_CFA*CF1_RE
     &       +      UPW * UQ_CFA*CF2_RE
C
       UQ_T1 = UQ_T1             + 0.5*UQ_RTA*RT1_T1
       UQ_D1 = UQ_D1 + 0.5*UQ_DA
       UQ_U1 = UQ_U1             + 0.5*UQ_RTA*RT1_U1
       UQ_T2 = UQ_T2             + 0.5*UQ_RTA*RT2_T2
       UQ_D2 = UQ_D2 + 0.5*UQ_DA
       UQ_U2 = UQ_U2             + 0.5*UQ_RTA*RT2_U2
       UQ_MS = UQ_MS             + 0.5*UQ_RTA*RT1_MS
     &                           + 0.5*UQ_RTA*RT2_MS
       UQ_RE = UQ_RE             + 0.5*UQ_RTA*RT1_RE
     &                           + 0.5*UQ_RTA*RT2_RE
C
       SCC = SCCON*1.333/(1.0+USA)
       SCC_USA = -SCC/(1.0+USA)
C
       SCC_US1 = SCC_USA*0.5
       SCC_US2 = SCC_USA*0.5
C
C
       SLOG = LOG(S2/S1)
       DXI = X2 - X1
C
       REZC = SCC*(CQA - SA*ALD)*DXI
     &      - DEA*2.0*          SLOG
     &      + DEA*2.0*(UQ*DXI - ULOG)*DUXCON
C
      TEQ1SRC = CQA - SA*ALD
      TEQ1PRD = SCC*TEQ1SRC*DXI
      TEQ1LOG = DEA*2.0*SLOG
      TEQ1CNV = UQ*DXI - ULOG
      TEQ1DUX = DEA*2.0*TEQ1CNV*DUXCON
      TEQ1SUB1 = TEQ1PRD - TEQ1LOG
      TEQ1REZ1 = TEQ1SUB1 + TEQ1DUX
      TEQ1SUB2 = SCC*(CQA - SA*ALD)*DXI - TEQ1LOG
      TEQ1REZ2 = TEQ1SUB2 + TEQ1DUX
      TEQ1SUB3 = SCC*(CQA - SA*ALD)*DXI - DEA*2.0*SLOG
      TEQ1REZ3 = TEQ1SUB3 + DEA*2.0*(UQ*DXI - ULOG)*DUXCON
      CALL TRACE_BLDIF_EQ1_RESIDUAL_TERMS('BLDIF',
     & TRACE_SIDE, TRACE_STATION, TRACE_PHASE, ITYP, X1, X2,
     & SCC, CQA, UPW, 1.0-UPW,
     & S1, S2, (1.0-UPW)*S1, UPW*S2,
     & CQ1, CQ2,
     & (1.0-UPW)*CQ1, UPW*CQ2,
     & SA, ALD, DXI, DEA, SLOG, UQ, ULOG,
     & TEQ1SRC, TEQ1PRD, TEQ1LOG, TEQ1CNV, TEQ1DUX,
     & TEQ1SUB1, TEQ1REZ1, TEQ1SUB2, TEQ1REZ2,
     & TEQ1SUB3, TEQ1REZ3, REZC)
C

c        if(  ! (rt2.gt.1.0e3 .and. rt1.le.1.0e3) .or.
c     &     (rt2.gt.1.0e4 .and. rt1.le.1.0e4) .or.
c     &     (rt2.gt.1.0e5 .and. rt1.le.1.0e5)        ) then
c           gga = (HKA-1.0-GCC/RTA)/HKA / sqrt(0.5*CFA)
c           write(*,4455) rta, hka, gga, cfa, cqa, sa, uq, ulog/dxi
c 4455      format(1x,f7.0, 2f9.4,f10.6,2f8.5,2f10.5)
c        endif


       Z_CFA = DEA*2.0*UQ_CFA*DXI * DUXCON
       Z_HKA = DEA*2.0*UQ_HKA*DXI * DUXCON
       Z_DA  = DEA*2.0*UQ_DA *DXI * DUXCON
       Z_SL = -DEA*2.0
       Z_UL = -DEA*2.0 * DUXCON
       Z_DXI = SCC    *(CQA - SA*ALD)     + DEA*2.0*UQ*DUXCON
       Z_USA = SCC_USA*(CQA - SA*ALD)*DXI
       Z_CQA = SCC*DXI
       Z_SA = -SCC*DXI*ALD
       Z_DEA = 2.0*((UQ*DXI - ULOG)*DUXCON - SLOG)
      IF(TRACE_SIDE.EQ.2 .AND. TRACE_STATION.EQ.98) THEN
       WRITE(0,'(A,6(A,Z8))')
     &   'F_EQ1X98',
     &   ' X1=',TRANSFER(X1,1),
     &   ' X2=',TRANSFER(X2,1),
     &   ' dxi=',TRANSFER(DXI,1),
     &   ' slog=',TRANSFER(SLOG,1),
     &   ' zDea=',TRANSFER(Z_DEA,1),
     &   ' zUsa=',TRANSFER(Z_USA,1)
      ENDIF
C
       Z_UPW = Z_CQA*(CQ2-CQ1) + Z_SA *(S2 -S1 )
     &       + Z_CFA*(CF2-CF1) + Z_HKA*(HK2-HK1)
C---- n6h20 z_s dump at IBL=66 IS=2 MRCHDU 10 iter 2 (TRDIF turbulent half, ityp=2)
      IF(TRACE_SIDE.EQ.2 .AND. TRACE_STATION.EQ.66
     &   .AND. TRACE_OUTER.EQ.10 .AND. TRACE_ITER.EQ.2
     &   .AND. ITYP.EQ.2) THEN
        WRITE(0,'(A,10(1X,A,Z8.8))') 'F_ZS_N66_IT2',
     &    'upw=',TRANSFER(UPW,1),
     &    'oneMinusUpw=',TRANSFER(1.0-UPW,1),
     &    'z_sa=',TRANSFER(Z_SA,1),
     &    'z_sl=',TRANSFER(Z_SL,1),
     &    's1=',TRANSFER(S1,1),
     &    's2=',TRANSFER(S2,1),
     &    'z_s1_storedTerm=',TRANSFER((1.0-UPW)*Z_SA,1),
     &    'z_s1_logTerm=',TRANSFER(Z_SL/S1,1),
     &    'z_s1=',TRANSFER((1.0-UPW)*Z_SA - Z_SL/S1,1),
     &    'z_s2=',TRANSFER(UPW*Z_SA + Z_SL/S2,1)
      ENDIF
      ZUPWCQDL = CQ2-CQ1
      ZUPWSDLT = S2-S1
      ZUPWCFDL = CF2-CF1
      ZUPWHKDL = HK2-HK1
      ZUPWCQTM = Z_CQA*ZUPWCQDL
      ZUPWSTRM = Z_SA*ZUPWSDLT
      ZUPWCFTM = Z_CFA*ZUPWCFDL
      ZUPWHKTM = Z_HKA*ZUPWHKDL
      ZUPWSM12 = ZUPWCQTM + ZUPWSTRM
      ZUPWS123 = ZUPWSM12 + ZUPWCFTM
      CALL TRACE_BLDIF_Z_UPW_TERMS('BLDIF', ITYP,
     & Z_CQA, ZUPWCQDL, ZUPWCQTM,
     & Z_SA, ZUPWSDLT, ZUPWSTRM,
     & Z_CFA, ZUPWCFDL, ZUPWCFTM,
     & Z_HKA, ZUPWHKDL, ZUPWHKTM,
     & ZUPWSM12, ZUPWS123, Z_UPW)
       Z_DE1 = 0.5*Z_DEA
       Z_DE2 = 0.5*Z_DEA
       Z_US1 = 0.5*Z_USA
       Z_US2 = 0.5*Z_USA
       Z_D1  = 0.5*Z_DA
       Z_D2  = 0.5*Z_DA
       Z_U1  =                 - Z_UL/U1
       Z_U2  =                   Z_UL/U2
       Z_X1  = -Z_DXI
       Z_X2  =  Z_DXI
      TEQ1ZXBASE = SCC*TEQ1SRC
      TEQ1ZXDUX = DEA*2.0*UQ*DUXCON
       Z_S1  = (1.0-UPW)*Z_SA  - Z_SL/S1
       Z_S2  =      UPW *Z_SA  + Z_SL/S2
      IF(TRACE_SIDE.EQ.1 .AND. TRACE_STATION.EQ.59
     &   .AND. TRACE_ITER.EQ.1) THEN
        WRITE(*,'(A,I1,5(1X,Z8.8))')
     &   'F_ZS2 p',TRACE_PHASE,
     &   TRANSFER(UPW,1), TRANSFER(Z_SA,1),
     &   TRANSFER(Z_SL,1), TRANSFER(S2,1),
     &   TRANSFER(Z_S2,1)
      ENDIF
      ROW11STERM = (1.0-UPW)*Z_SA
      ROW11LTERM = Z_SL/S1
      ROW21STERM = UPW*Z_SA
      ROW21LTERM = Z_SL/S2
C     ZS2 trace disabled
      CALL TRACE_BLDIF_EQ1_S_TERMS('BLDIF', ITYP,
     & (1.0-UPW), UPW, Z_SA, Z_SL, S1, S2,
     & ROW11STERM, ROW11LTERM, Z_S1,
     & ROW21STERM, ROW21LTERM, Z_S2)
       Z_CQ1 = (1.0-UPW)*Z_CQA
       Z_CQ2 =      UPW *Z_CQA
       Z_CF1 = (1.0-UPW)*Z_CFA
       Z_CF2 =      UPW *Z_CFA
       Z_HK1 = (1.0-UPW)*Z_HKA
       Z_HK2 =      UPW *Z_HKA
      CALL TRACE_BLDIF_EQ1_X_TERMS('BLDIF', ITYP,
     & TEQ1ZXBASE, TEQ1ZXDUX, Z_DXI, Z_X1, Z_X2)
C
       VS1(1,1) = Z_S1
       VS1(1,2) =        Z_UPW*UPW_T1 + Z_DE1*DE1_T1 + Z_US1*US1_T1
       VS1(1,3) = Z_D1 + Z_UPW*UPW_D1 + Z_DE1*DE1_D1 + Z_US1*US1_D1
       IF(TRACE_SIDE.EQ.2 .AND. TRACE_STATION.EQ.90) THEN
        WRITE(0,'(A,Z8,1X,Z8,1X,Z8,1X,Z8,1X,Z8)')
     &   'F_DE_DETAIL ',
     &   TRANSFER(Z_DE1,1),
     &   TRANSFER(Z_DEA,1),
     &   TRANSFER((UQ*DXI-ULOG)*DUXCON,1),
     &   TRANSFER(SLOG,1),
     &   TRANSFER((UQ*DXI-ULOG)*DUXCON-SLOG,1)
        WRITE(0,'(A,Z8,1X,Z8,1X,Z8,1X,Z8,1X,Z8)')
     &   'F_ROW13_TRANS ',
     &   TRANSFER(Z_D1,1),
     &   TRANSFER(Z_UPW*UPW_D1,1),
     &   TRANSFER(Z_DE1*DE1_D1,1),
     &   TRANSFER(Z_US1*US1_D1,1),
     &   TRANSFER(VS1(1,3),1)
       ENDIF
       VS1(1,4) = Z_U1 + Z_UPW*UPW_U1 + Z_DE1*DE1_U1 + Z_US1*US1_U1
       VS1(1,5) = Z_X1
       VS2(1,1) = Z_S2
       VS2(1,2) =        Z_UPW*UPW_T2 + Z_DE2*DE2_T2 + Z_US2*US2_T2
C---- n6h20 base VS2(1,2) trace at IBL=66 iter 2 mc=10
       IF(TRACE_SIDE.EQ.2 .AND. TRACE_STATION.EQ.66
     &    .AND. TRACE_ITER.EQ.2 .AND. TRACE_OUTER.EQ.10) THEN
        WRITE(0,'(A,4(A,Z8))') 'F_BASE10_66_T',
     &   ' ZUPW=',TRANSFER(Z_UPW,1),
     &   ' UPW_T2=',TRANSFER(UPW_T2,1),
     &   ' ZDE2=',TRANSFER(Z_DE2,1),
     &   ' DE2_T2=',TRANSFER(DE2_T2,1)
        WRITE(0,'(A,3(A,Z8))') 'F_BASE10_66_T',
     &   ' ZUS2=',TRANSFER(Z_US2,1),
     &   ' US2_T2=',TRANSFER(US2_T2,1),
     &   ' VS12=',TRANSFER(VS2(1,2),1)
       ENDIF
      IF(TRACE_SIDE.EQ.2 .AND. TRACE_STATION.EQ.98) THEN
       WRITE(0,'(A,6(A,Z8))')
     &   'F_EQ1T98',
     &   ' upw=',TRANSFER(Z_UPW*UPW_T2,1),
     &   ' de=',TRANSFER(Z_DE2*DE2_T2,1),
     &   ' us=',TRANSFER(Z_US2*US2_T2,1),
     &   ' trn=',TRANSFER(VS2(1,2),1),
     &   ' zUpw=',TRANSFER(Z_UPW,1),
     &   ' uT2=',TRANSFER(UPW_T2,1)
       WRITE(0,'(A,6(A,Z8))')
     &   'F_EQ1D98',
     &   ' zDe=',TRANSFER(Z_DE2,1),
     &   ' deT=',TRANSFER(DE2_T2,1),
     &   ' zUs=',TRANSFER(Z_US2,1),
     &   ' usT=',TRANSFER(US2_T2,1),
     &   ' DI2=',TRANSFER(DI2,1),
     &   ' US2=',TRANSFER(US2,1)
      ENDIF
      IF(TRACE_SIDE.EQ.2 .AND. (TRACE_STATION.EQ.85
     &   .OR. TRACE_STATION.EQ.86) .AND. TRACE_ITER.EQ.1) THEN
       WRITE(0,'(A,I3,A,Z8,A,Z8,A,Z8,A,Z8)')
     &   'F_V22T I=',TRACE_STATION,
     &   ' trn=',TRANSFER(VS2(1,2),1),
     &   ' upw=',TRANSFER(Z_UPW*UPW_T2,1),
     &   ' de=',TRANSFER(Z_DE2*DE2_T2,1),
     &   ' us=',TRANSFER(Z_US2*US2_T2,1)
       WRITE(0,'(A,I3,A,Z8,A,Z8,A,Z8,A,Z8)')
     &   'F_V22F I=',TRACE_STATION,
     &   ' zupw=',TRANSFER(Z_UPW,1),
     &   ' upwt2=',TRANSFER(UPW_T2,1),
     &   ' zus2=',TRANSFER(Z_US2,1),
     &   ' us2t2=',TRANSFER(US2_T2,1)
      ENDIF
       VS2(1,3) = Z_D2 + Z_UPW*UPW_D2 + Z_DE2*DE2_D2 + Z_US2*US2_D2
C---- GDB: dump transport terms at station 30 side 1
       IF(TRACE_SIDE.EQ.1 .AND. TRACE_STATION.EQ.97) THEN
         WRITE(0,'(A,Z8,A,Z8,A,Z8,A,Z8,A,Z8,A,Z8,A,Z8)')
     &    'F_TR zd2=',TRANSFER(Z_D2,1),
     &    ' upw=',TRANSFER(Z_UPW*UPW_D2,1),
     &    ' zupw=',TRANSFER(Z_UPW,1),
     &    ' upwd=',TRANSFER(UPW_D2,1),
     &    ' de=',TRANSFER(Z_DE2*DE2_D2,1),
     &    ' us=',TRANSFER(Z_US2*US2_D2,1),
     &    ' tot=',TRANSFER(VS2(1,3),1)
       ENDIF
       VS2(1,4) = Z_U2 + Z_UPW*UPW_U2 + Z_DE2*DE2_U2 + Z_US2*US2_U2
       VS2(1,5) = Z_X2
       VSM(1)   =        Z_UPW*UPW_MS + Z_DE1*DE1_MS + Z_US1*US1_MS
     &                                + Z_DE2*DE2_MS + Z_US2*US2_MS
C
       VS1(1,2) = VS1(1,2) + Z_CQ1*CQ1_T1 + Z_CF1*CF1_T1 + Z_HK1*HK1_T1
       VS1(1,3) = VS1(1,3) + Z_CQ1*CQ1_D1 + Z_CF1*CF1_D1 + Z_HK1*HK1_D1
       IF(TRACE_SIDE.EQ.2 .AND. TRACE_STATION.EQ.90) THEN
        WRITE(0,'(A,Z8,1X,Z8,1X,Z8,1X,Z8)')
     &   'F_ROW13_CORR ',
     &   TRANSFER(Z_CQ1*CQ1_D1,1),
     &   TRANSFER(Z_CF1*CF1_D1,1),
     &   TRANSFER(Z_HK1*HK1_D1,1),
     &   TRANSFER(VS1(1,3),1)
       ENDIF
       VS1(1,4) = VS1(1,4) + Z_CQ1*CQ1_U1 + Z_CF1*CF1_U1 + Z_HK1*HK1_U1
C
C---- n6h20 correction terms trace BEFORE update at IBL=66 iter 2 mc=10
       IF(TRACE_SIDE.EQ.2 .AND. TRACE_STATION.EQ.66
     &    .AND. TRACE_ITER.EQ.2 .AND. TRACE_OUTER.EQ.10) THEN
        WRITE(0,'(A,3(A,Z8))') 'F_CORR10_66',
     &   ' ZCQ2=',TRANSFER(Z_CQ2,1),
     &   ' CQ2T2=',TRANSFER(CQ2_T2,1),
     &   ' ZCF2=',TRANSFER(Z_CF2,1)
        WRITE(0,'(A,3(A,Z8))') 'F_CORR10_66',
     &   ' CF2T2=',TRANSFER(CF2_T2,1),
     &   ' ZHK2=',TRANSFER(Z_HK2,1),
     &   ' HK2T2=',TRANSFER(HK2_T2,1)
        WRITE(0,'(A,4(A,Z8))') 'F_CORR10_66',
     &   ' CqT=',TRANSFER(Z_CQ2*CQ2_T2,1),
     &   ' CfT=',TRANSFER(Z_CF2*CF2_T2,1),
     &   ' HkT=',TRANSFER(Z_HK2*HK2_T2,1),
     &   ' BASE=',TRANSFER(VS2(1,2),1)
       ENDIF
       VS2(1,2) = VS2(1,2) + Z_CQ2*CQ2_T2 + Z_CF2*CF2_T2 + Z_HK2*HK2_T2
       IF(TRACE_SIDE.EQ.2 .AND. TRACE_STATION.EQ.66
     &    .AND. TRACE_ITER.EQ.2 .AND. TRACE_OUTER.EQ.10) THEN
        WRITE(0,'(A,A,Z8)') 'F_CORR10_66',
     &   ' FINAL=',TRANSFER(VS2(1,2),1)
       ENDIF
      IF(TRACE_SIDE.EQ.2 .AND. (TRACE_STATION.EQ.85
     &   .OR. TRACE_STATION.EQ.86) .AND. TRACE_ITER.EQ.1) THEN
       WRITE(0,'(A,I3,A,Z8,A,Z8,A,Z8,A,Z8)')
     &   'F_V22C I=',TRACE_STATION,
     &   ' fin=',TRANSFER(VS2(1,2),1),
     &   ' cq=',TRANSFER(Z_CQ2*CQ2_T2,1),
     &   ' cf=',TRANSFER(Z_CF2*CF2_T2,1),
     &   ' hk=',TRANSFER(Z_HK2*HK2_T2,1)
      ENDIF
       VS2(1,3) = VS2(1,3) + Z_CQ2*CQ2_D2 + Z_CF2*CF2_D2 + Z_HK2*HK2_D2
C---- GDB: dump VS2(1,3) derivative terms at station 30 side 1
       IF(TRACE_SIDE.EQ.1 .AND. TRACE_STATION.EQ.97) THEN
         WRITE(0,'(A,Z8,A,Z8,A,Z8,A,Z8,A,Z8,A,Z8,A,Z8)')
     &    'F_V13 base=',
     &    TRANSFER(VS2(1,3)-Z_CQ2*CQ2_D2-Z_CF2*CF2_D2-Z_HK2*HK2_D2
     &    ,1),
     &    ' cq=',TRANSFER(Z_CQ2*CQ2_D2,1),
     &    ' cf=',TRANSFER(Z_CF2*CF2_D2,1),
     &    ' hk=',TRANSFER(Z_HK2*HK2_D2,1),
     &    ' zcq=',TRANSFER(Z_CQ2,1),
     &    ' cqd=',TRANSFER(CQ2_D2,1),
     &    ' res=',TRANSFER(VS2(1,3),1)
       ENDIF
       IF(TRACE_SIDE.EQ.2 .AND. (TRACE_STATION.EQ.85
     &    .OR. TRACE_STATION.EQ.86) .AND. TRACE_ITER.EQ.1) THEN
        WRITE(0,'(A,I3,A,Z8,A,Z8,A,Z8,A,Z8)')
     &   'F_V24T I=',TRACE_STATION,
     &   ' trn=',TRANSFER(VS2(1,4),1),
     &   ' base=',TRANSFER(Z_U2,1),
     &   ' us=',TRANSFER(Z_US2*US2_U2,1),
     &   ' de=',TRANSFER(Z_DE2*DE2_U2,1)
       ENDIF
       VS2(1,4) = VS2(1,4) + Z_CQ2*CQ2_U2 + Z_CF2*CF2_U2 + Z_HK2*HK2_U2
       IF(TRACE_SIDE.EQ.2 .AND. (TRACE_STATION.EQ.85
     &    .OR. TRACE_STATION.EQ.86) .AND. TRACE_ITER.EQ.1) THEN
        WRITE(0,'(A,I3,A,Z8)')
     &   'F_V24C I=',TRACE_STATION,' fin=',TRANSFER(VS2(1,4),1)
       ENDIF
C
      CALL TRACE_BLDIF_EQ1_T_TERMS('BLDIF', ITYP,
     & Z_DE1, DE1_T1, Z_UPW*UPW_T1, Z_DE1*DE1_T1, Z_US1*US1_T1,
     & Z_UPW*UPW_T1 + Z_DE1*DE1_T1 + Z_US1*US1_T1,
     & Z_CQ1, CQ1_T1, Z_CQ1*CQ1_T1,
     & Z_CF1, CF1_T1, Z_CF1*CF1_T1,
     & Z_HK1, HK1_T1, Z_HK1*HK1_T1, VS1(1,2),
     & Z_DE2, DE2_T2, Z_UPW*UPW_T2, Z_DE2*DE2_T2, Z_US2*US2_T2,
     & Z_UPW*UPW_T2 + Z_DE2*DE2_T2 + Z_US2*US2_T2,
     & Z_CQ2, CQ2_T2, Z_CQ2*CQ2_T2,
     & Z_CF2, CF2_T2, Z_CF2*CF2_T2,
     & Z_HK2, HK2_T2, Z_HK2*HK2_T2, VS2(1,2))
      ROW13BASETERM = Z_D1
      ROW13UPWTERM = Z_UPW*UPW_D1
      ROW13DETERM = Z_DE1*DE1_D1
      ROW13USTERM = Z_US1*US1_D1
      ROW13TRANSPORT = ROW13BASETERM + ROW13UPWTERM
     &               + ROW13DETERM + ROW13USTERM
      ROW13CQTERM = Z_CQ1*CQ1_D1
      ROW13CFTERM = Z_CF1*CF1_D1
      ROW13HKTERM = Z_HK1*HK1_D1
      ROW23BASETERM = Z_D2
      ROW23UPWTERM = Z_UPW*UPW_D2
      ROW23DETERM = Z_DE2*DE2_D2
      ROW23USTERM = Z_US2*US2_D2
      ROW23TRANSPORT = ROW23BASETERM + ROW23UPWTERM
     &               + ROW23DETERM + ROW23USTERM
      ROW23CQTERM = Z_CQ2*CQ2_D2
      ROW23CFTERM = Z_CF2*CF2_D2
      ROW23HKTERM = Z_HK2*HK2_D2
      CALL TRACE_BLDIF_EQ1_D_TERMS('BLDIF', ITYP,
     & Z_D1, Z_UPW, UPW_D1, Z_DE1, DE1_D1, Z_US1, US1_D1,
     & Z_CQ1, CQ1_D1, Z_CF1, CF1_D1, Z_HK1, HK1_D1,
     & ROW13BASETERM, ROW13UPWTERM, ROW13DETERM, ROW13USTERM,
     & ROW13TRANSPORT, ROW13CQTERM, ROW13CFTERM, ROW13HKTERM, VS1(1,3),
     & Z_D2, UPW_D2, Z_DE2, DE2_D2, Z_US2, US2_D2,
     & Z_CQ2, CQ2_D2, Z_CF2, CF2_D2, Z_HK2, HK2_D2,
     & ROW23BASETERM, ROW23UPWTERM, ROW23DETERM, ROW23USTERM,
     & ROW23TRANSPORT, ROW23CQTERM, ROW23CFTERM, ROW23HKTERM, VS2(1,3))
      CALL TRACE_BLDIF_EQ1_U_TERMS('BLDIF', ITYP,
     & Z_U1, Z_UPW, UPW_U1, Z_DE1, DE1_U1, Z_US1, US1_U1,
     & Z_CQ1, CQ1_U1, Z_CF1, CF1_U1, Z_HK1, HK1_U1,
     & Z_U1, Z_UPW*UPW_U1, Z_DE1*DE1_U1, Z_US1*US1_U1,
     & Z_U1 + Z_UPW*UPW_U1 + Z_DE1*DE1_U1 + Z_US1*US1_U1,
     & Z_CQ1*CQ1_U1, Z_CF1*CF1_U1, Z_HK1*HK1_U1, VS1(1,4),
     & Z_U2, UPW_U2, Z_DE2, DE2_U2, Z_US2, US2_U2,
     & Z_CQ2, CQ2_U2, Z_CF2, CF2_U2, Z_HK2, HK2_U2,
     & Z_U2, Z_UPW*UPW_U2, Z_DE2*DE2_U2, Z_US2*US2_U2,
     & Z_U2 + Z_UPW*UPW_U2 + Z_DE2*DE2_U2 + Z_US2*US2_U2,
     & Z_CQ2*CQ2_U2, Z_CF2*CF2_U2, Z_HK2*HK2_U2, VS2(1,4))
      CALL TRACE_BLDIF_EQ1_ROWS('BLDIF', ITYP,
     & VS1(1,1), VS1(1,2), VS1(1,3), VS1(1,4),
     & VS2(1,1), VS2(1,2), VS2(1,3), VS2(1,4))
C
       VSM(1)   = VSM(1)   + Z_CQ1*CQ1_MS + Z_CF1*CF1_MS + Z_HK1*HK1_MS
     &                     + Z_CQ2*CQ2_MS + Z_CF2*CF2_MS + Z_HK2*HK2_MS
       VSR(1)   =            Z_CQ1*CQ1_RE + Z_CF1*CF1_RE
     &                     + Z_CQ2*CQ2_RE + Z_CF2*CF2_RE
       VSX(1)   = 0.
       VSREZ(1) = -REZC
C
      ENDIF
C
C**** Set up momentum equation
      HA = 0.5*(H1 + H2)
      MA = 0.5*(M1 + M2)
      XA = 0.5*(X1 + X2)
      TA = 0.5*(T1 + T2)
      HWA = 0.5*(DW1/T1 + DW2/T2)
C
C---- set Cf term, using central value CFM for better accuracy in drag
      CFX     = 0.50*CFM*XA/TA  +  0.25*(CF1*X1/T1 + CF2*X2/T2)
      CFX_XA  = 0.50*CFM   /TA
      CFX_TA  = -.50*CFM*XA/TA**2
C
      CFX_X1  = 0.25*CF1   /T1     + CFX_XA*0.5
      CFX_X2  = 0.25*CF2   /T2     + CFX_XA*0.5
      CFX_T1  = -.25*CF1*X1/T1**2  + CFX_TA*0.5
      CFX_T2  = -.25*CF2*X2/T2**2  + CFX_TA*0.5
      CFX_CF1 = 0.25*    X1/T1
      CFX_CF2 = 0.25*    X2/T2
      CFX_CFM = 0.50*    XA/TA
C
      BTMP = HA + 2.0 - MA + HWA
C
      CALL TRACE_BLDIF_EQ2_INPUT_BUNDLE('BLDIF',
     &     TRACE_SIDE, TRACE_STATION, ITYP,
     &     X1, X2, U1, U2, T1, T2, DW1, DW2,
     &     H1, H1_T1, H1_D1, H2, H2_T2, H2_D2,
     &     M1, M1_U1, M2, M2_U2,
     &     CFM, CFM_T1, CFM_D1, CFM_U1,
     &     CFM_T2, CFM_D2, CFM_U2,
     &     CF1, CF1_T1, CF1_D1, CF1_U1,
     &     CF2, CF2_T2, CF2_D2, CF2_U2,
     &     XLOG, ULOG, TLOG, DDLOG)
      REZT  = TLOG + BTMP*ULOG - XLOG*0.5*CFX
      IF((TRACE_SIDE.EQ.1.AND.TRACE_STATION.EQ.3
     &   .AND.TRACE_ITER.EQ.1)
     &  .OR.(TRACE_SIDE.EQ.1.AND.TRACE_STATION.EQ.58
     &   .AND.TRACE_ITER.EQ.3)
     &  .OR.(TRACE_SIDE.EQ.2.AND.TRACE_STATION.EQ.83
     &   .AND.TRACE_ITER.EQ.1)) THEN
        WRITE(*,'(A,I3,A,I2,6(1X,Z8.8))')
     &   'F_REZT',TRACE_STATION,' i',TRACE_ITER,
     &   TRANSFER(TLOG,1), TRANSFER(ULOG,1),
     &   TRANSFER(XLOG,1), TRANSFER(BTMP,1),
     &   TRANSFER(CFX,1), TRANSFER(REZT,1)
      ENDIF
      CALL TRACE_BLDIF_EQ2_RESIDUAL_TERMS('BLDIF', ITYP,
     &     HA, MA, XA, TA, HWA,
     &     0.50*CFM*XA/TA,
     &     0.25*(CF1*X1/T1 + CF2*X2/T2),
     &     CFX, BTMP, TLOG, ULOG, XLOG, REZT)
      Z_CFX = -XLOG*0.5
      Z_HA  =  ULOG
      Z_HWA =  ULOG
      Z_MA  = -ULOG
      Z_XL  =-DDLOG * 0.5*CFX
      Z_UL  = DDLOG * BTMP
      Z_TL  = DDLOG
C
      Z_CFM = Z_CFX*CFX_CFM
      Z_CF1 = Z_CFX*CFX_CF1
      Z_CF2 = Z_CFX*CFX_CF2
C
      Z_T1 = -Z_TL/T1 + Z_CFX*CFX_T1 + Z_HWA*0.5*(-DW1/T1**2)
      Z_T2 =  Z_TL/T2 + Z_CFX*CFX_T2 + Z_HWA*0.5*(-DW2/T2**2)
      ZX1_XLOGTERM = -Z_XL/X1
      ZX1_CFXTERM = Z_CFX*CFX_X1
      Z_X1 = ZX1_XLOGTERM + ZX1_CFXTERM
      ZX2_XLOGTERM = Z_XL/X2
      ZX2_CFXTERM = Z_CFX*CFX_X2
      Z_X2 = ZX2_XLOGTERM + ZX2_CFXTERM
      Z_U1 = -Z_UL/U1
      Z_U2 =  Z_UL/U2
      CALL TRACE_BLDIF_EQ2_ZT2_TERMS('BLDIF',
     &     Z_TL, T2, Z_CFX, CFX_T2, Z_HWA, DW2,
     &     Z_TL/T2, Z_CFX*CFX_T2, Z_HWA*0.5*(-DW2/T2**2), Z_T2)
C
      VS1(2,2) = 0.5*Z_HA*H1_T1 + Z_CFM*CFM_T1 + Z_CF1*CF1_T1 + Z_T1
      IF(TRACE_SIDE.EQ.1 .AND. TRACE_STATION.EQ.56
     &   .AND. TRACE_ITER.EQ.1) THEN
        WRITE(*,'(A,4(1X,Z8),A,4(1X,Z8),A,Z8)')
     &   'F_VS1_22',
     &   TRANSFER(0.5*Z_HA,1), TRANSFER(Z_CFM,1),
     &   TRANSFER(Z_CF1,1), TRANSFER(Z_T1,1),
     &   ' D=', TRANSFER(H1_T1,1), TRANSFER(CFM_T1,1),
     &   TRANSFER(CF1_T1,1), TRANSFER(0.0,1),
     &   ' S=', TRANSFER(VS1(2,2),1)
      ENDIF
      CALL TRACE_BLDIF_EQ2_T1_TERMS('BLDIF',
     &     0.5*Z_HA, Z_CFM, Z_CF1, Z_T1,
     &     H1_T1, CFM_T1, CF1_T1,
     &     0.5*Z_HA*H1_T1, Z_CFM*CFM_T1, Z_CF1*CF1_T1,
     &     VS1(2,2))
      VS1(2,3) = 0.5*Z_HA*H1_D1 + Z_CFM*CFM_D1 + Z_CF1*CF1_D1
      CALL TRACE_BLDIF_EQ2_D1_TERMS('BLDIF',
     &     TRACE_SIDE, TRACE_STATION, ITYP,
     &     0.5*Z_HA, Z_CFM, Z_CF1,
     &     H1_D1, CFM_D1, CF1_D1,
     &     0.5*Z_HA*H1_D1, Z_CFM*CFM_D1, Z_CF1*CF1_D1,
     &     VS1(2,3))
      VS1(2,4) = 0.5*Z_MA*M1_U1 + Z_CFM*CFM_U1 + Z_CF1*CF1_U1 + Z_U1
      VS1(2,5) =                                                Z_X1
      VS2(2,2) = 0.5*Z_HA*H2_T2 + Z_CFM*CFM_T2 + Z_CF2*CF2_T2 + Z_T2
      IF(TRACE_STATION.EQ.58 .AND. TRACE_ITER.EQ.4) THEN
        WRITE(*,'(A,I1,A,I1,A,Z8,A,Z8,A,Z8)')
     &   'F_VS222 s',TRACE_SIDE,' p',TRACE_PHASE,
     &   ' T2=',TRANSFER(T2,1),
     &   ' D2=',TRANSFER(D2,1),
     &   ' VS222=',TRANSFER(VS2(2,2),1)
      ENDIF
      ISTRACE = TRACE_SIDE
      IBLTRACE = TRACE_STATION
      ITYPTRACE = ITYP
      CALL TRACE_BLDIF_EQ2_T2_TERMS('BLDIF',
     &     ISTRACE, IBLTRACE, ITYPTRACE,
     &     0.5*Z_HA, Z_CFM, Z_CF2, Z_T2,
     &     H2_T2, CFM_T2, CF2_T2,
     &     0.5*Z_HA*H2_T2, Z_CFM*CFM_T2, Z_CF2*CF2_T2,
     &     VS2(2,2))
      VS2(2,3) = 0.5*Z_HA*H2_D2 + Z_CFM*CFM_D2 + Z_CF2*CF2_D2
      CALL TRACE_BLDIF_EQ2_D2_TERMS('BLDIF',
     &     0.5*Z_HA, Z_CFM, Z_CF2,
     &     H2_D2, CFM_D2, CF2_D2,
     &     0.5*Z_HA*H2_D2, Z_CFM*CFM_D2, Z_CF2*CF2_D2,
     &     VS2(2,3))
      VS2(2,4) = 0.5*Z_MA*M2_U2 + Z_CFM*CFM_U2 + Z_CF2*CF2_U2 + Z_U2
      VS2(2,5) =                                                Z_X2
      CALL TRACE_BLDIF_EQ2_X_TERMS('BLDIF', ITYP,
     &     Z_XL, Z_CFX, Z_X1, Z_X2)
      CALL TRACE_BLDIF_EQ2_X_BREAKDOWN('BLDIF', ITYP,
     &     CFX_X1, ZX1_XLOGTERM, ZX1_CFXTERM, Z_X1,
     &     CFX_X2, ZX2_XLOGTERM, ZX2_CFXTERM, Z_X2)
      CALL TRACE_BLDIF_EQ2_U_TERMS('BLDIF',
     &     Z_CFX, CFX_CFM, CFX_CF1, CFX_CF2,
     &     Z_CFM, Z_CF1, Z_CF2,
     &     CFM_U1, CFM_U2, CF1_U1, CF2_U2,
     &     0.5*Z_MA*M1_U1, Z_CFM*CFM_U1, Z_CF1*CF1_U1, Z_U1, VS1(2,4),
     &     0.5*Z_MA*M2_U2, Z_CFM*CFM_U2, Z_CF2*CF2_U2, Z_U2, VS2(2,4))
C
      VSM(2)   = 0.5*Z_MA*M1_MS + Z_CFM*CFM_MS + Z_CF1*CF1_MS
     &         + 0.5*Z_MA*M2_MS                + Z_CF2*CF2_MS
      VSR(2)   =                  Z_CFM*CFM_RE + Z_CF1*CF1_RE
     &                                         + Z_CF2*CF2_RE
      VSX(2)   = 0.
      VSREZ(2) = -REZT
C
C**** Set up shape parameter equation
C
      XOT1 = X1/T1
      XOT2 = X2/T2
C
      HA  = 0.5*(H1  + H2 )
      HSA = 0.5*(HS1 + HS2)
      HCA = 0.5*(HC1 + HC2)
      HWA = 0.5*(DW1/T1 + DW2/T2)
C
      IF(TRACE_SIDE.EQ.2 .AND. TRACE_STATION.EQ.83
     &   .AND. TRACE_ITER.EQ.1) THEN
        WRITE(0,'(A,A,Z8,A,Z8,A,Z8,A,Z8,A,Z8,A,Z8,A,Z8,
     &   A,Z8,A,Z8)')
     &   'F_BLDIF83_IN',
     &   ' DI1=',TRANSFER(DI1,1),
     &   ' DI2=',TRANSFER(DI2,1),
     &   ' XOT1=',TRANSFER(XOT1,1),
     &   ' XOT2=',TRANSFER(XOT2,1),
     &   ' UPW=',TRANSFER(UPW,1),
     &   ' HS1=',TRANSFER(HS1,1),
     &   ' HS2=',TRANSFER(HS2,1),
     &   ' H1=',TRANSFER(H1,1),
     &   ' H2=',TRANSFER(H2,1)
      ENDIF
      DIX = (1.0-UPW)*DI1*XOT1 + UPW*DI2*XOT2
      CFX = (1.0-UPW)*CF1*XOT1 + UPW*CF2*XOT2
      DIX_UPW = DI2*XOT2 - DI1*XOT1
      CFX_UPW = CF2*XOT2 - CF1*XOT1
C
      BTMP = 2.0*HCA/HSA + 1.0 - HA - HWA
C
      REZH  = HLOG + BTMP*ULOG + XLOG*(0.5*CFX-DIX)
      IF(TRACE_SIDE.EQ.2 .AND. TRACE_STATION.EQ.5
     &   .AND. TRACE_ITER.EQ.1 .AND. ITYP.EQ.1) THEN
        WRITE(0,'(A,11(1X,Z8.8))') 'G_REZH_INPUTS',
     &    TRANSFER(XOT1,1), TRANSFER(XOT2,1),
     &    TRANSFER(UPW,1), TRANSFER(H1,1), TRANSFER(H2,1),
     &    TRANSFER(HS1,1), TRANSFER(HS2,1),
     &    TRANSFER(HC1,1), TRANSFER(HC2,1),
     &    TRANSFER(DW1,1), TRANSFER(DW2,1)
        WRITE(0,'(A,10(1X,Z8.8))') 'G_REZH_MIX',
     &    TRANSFER(HA,1), TRANSFER(HSA,1),
     &    TRANSFER(HCA,1), TRANSFER(HWA,1),
     &    TRANSFER(DI1,1), TRANSFER(DI2,1),
     &    TRANSFER(CF1,1), TRANSFER(CF2,1),
     &    TRANSFER(DIX,1), TRANSFER(CFX,1)
        WRITE(0,'(A,5(1X,Z8.8))') 'G_REZH',
     &    TRANSFER(REZH,1), TRANSFER(HLOG,1),
     &    TRANSFER(BTMP,1), TRANSFER(ULOG,1),
     &    TRANSFER(XLOG,1)
      ENDIF
      IF(TRACE_SIDE.EQ.2 .AND. TRACE_STATION.EQ.5
     &   .AND. ITYP.EQ.1) THEN
        WRITE(0,'(A,I1,A,I2,7(1X,Z8.8))')
     &   'F_REZH5L P=',TRACE_PHASE,' I=',TRACE_ITER,
     &   TRANSFER(HLOG,1), TRANSFER(BTMP,1),
     &   TRANSFER(ULOG,1), TRANSFER(XLOG,1),
     &   TRANSFER(CFX,1), TRANSFER(DIX,1),
     &   TRANSFER(REZH,1)
      ENDIF
      IF(TRACE_SIDE.EQ.2 .AND. (TRACE_STATION.EQ.85
     &   .OR. TRACE_STATION.EQ.86) .AND. TRACE_ITER.EQ.1) THEN
        WRITE(0,'(A,I3,A,I2,7(1X,Z8.8))')
     &   'F_REZH85 I=',TRACE_STATION,' P=',TRACE_PHASE,
     &   TRANSFER(HLOG,1), TRANSFER(BTMP,1),
     &   TRANSFER(ULOG,1), TRANSFER(XLOG,1),
     &   TRANSFER(CFX,1), TRANSFER(DIX,1),
     &   TRANSFER(REZH,1)
        WRITE(0,'(A,I3,4(1X,Z8.8))')
     &   'F_HLOG85 I=',TRACE_STATION,
     &   TRANSFER(HS1,1), TRANSFER(HS2,1),
     &   TRANSFER(HS2/HS1,1), TRANSFER(HLOG,1)
      ENDIF
      IF(TRACE_SIDE.EQ.2 .AND. TRACE_STATION.EQ.83
     &   .AND. TRACE_ITER.EQ.1) THEN
        WRITE(0,'(A,7(1X,Z8.8))')
     &   'F_REZH83',
     &   TRANSFER(HLOG,1), TRANSFER(BTMP,1),
     &   TRANSFER(ULOG,1), TRANSFER(XLOG,1),
     &   TRANSFER(CFX,1), TRANSFER(DIX,1),
     &   TRANSFER(REZH,1)
      ENDIF
      IF(TRACE_SIDE.EQ.1 .AND. TRACE_STATION.EQ.4
     &   .AND. TRACE_ITER.EQ.1) THEN
        WRITE(*,'(A,7(1X,Z8.8))')
     &   'F_REZH4',
     &   TRANSFER(HLOG,1), TRANSFER(BTMP,1),
     &   TRANSFER(ULOG,1), TRANSFER(XLOG,1),
     &   TRANSFER(CFX,1), TRANSFER(DIX,1),
     &   TRANSFER(REZH,1)
      ENDIF
      CALL TRACE_BLDIF_EQ3_RESIDUAL_TERMS('BLDIF', ITYP,
     &     HLOG, BTMP, ULOG, BTMP*ULOG, XLOG, CFX, 0.5*CFX,
     &     DIX, 0.5*CFX-DIX, XLOG*(0.5*CFX-DIX), REZH)
      Z_CFX =  XLOG*0.5
      Z_DIX = -XLOG
      Z_HCA = 2.0*ULOG/HSA
      Z_HA  = -ULOG
      Z_HWA = -ULOG
      Z_XL  = DDLOG * (0.5*CFX-DIX)
      Z_UL  = DDLOG * BTMP
      Z_HL  = DDLOG
      IF(TRACE_SIDE.EQ.1 .AND. TRACE_STATION.EQ.78
     &   .AND. TRACE_ITER.EQ.5) THEN
        WRITE(0,'(A,I1,8(1X,Z8.8))')
     &    'F_ZINP_78 it=',TRACE_ITER,
     &    TRANSFER(REAL(HS1),1), TRANSFER(REAL(HS2),1),
     &    TRANSFER(REAL(HCA),1), TRANSFER(REAL(HSA),1),
     &    TRANSFER(REAL(ULOG),1), TRANSFER(REAL(XLOG),1),
     &    TRANSFER(REAL(DDLOG),1), TRANSFER(REAL(UPW),1)
        WRITE(0,'(A,I1,6(1X,Z8.8))')
     &    'F_ZOUT_78 it=',TRACE_ITER,
     &    TRANSFER(REAL(Z_CFX),1), TRANSFER(REAL(Z_DIX),1),
     &    TRANSFER(REAL(Z_HCA),1), TRANSFER(REAL(Z_XL),1),
     &    TRANSFER(REAL(Z_UL),1), TRANSFER(REAL(Z_HL),1)
      ENDIF
C
      Z_UPW = Z_CFX*CFX_UPW + Z_DIX*DIX_UPW
C
      Z_HS1 = -HCA*ULOG/HSA**2 - Z_HL/HS1
      Z_HS2 = -HCA*ULOG/HSA**2 + Z_HL/HS2
C
      Z_CF1 = (1.0-UPW)*Z_CFX*XOT1
      Z_CF2 =      UPW *Z_CFX*XOT2
      Z_DI1 = (1.0-UPW)*Z_DIX*XOT1
      Z_DI2 =      UPW *Z_DIX*XOT2
C
      Z_T1 = (1.0-UPW)*(Z_CFX*CF1 + Z_DIX*DI1)*(-XOT1/T1)
      Z_T2 =      UPW *(Z_CFX*CF2 + Z_DIX*DI2)*(-XOT2/T2)
      Z_X1 = (1.0-UPW)*(Z_CFX*CF1 + Z_DIX*DI1)/ T1        - Z_XL/X1
      Z_X2 =      UPW *(Z_CFX*CF2 + Z_DIX*DI2)/ T2        + Z_XL/X2
      Z_U1 =                                              - Z_UL/U1
      Z_U2 =                                                Z_UL/U2
C
      Z_T1 = Z_T1 + Z_HWA*0.5*(-DW1/T1**2)
      Z_T2 = Z_T2 + Z_HWA*0.5*(-DW2/T2**2)
C
      ZTERM_CF1 = Z_CFX*CF1
      ZTERM_CF2 = Z_CFX*CF2
      ZTERM_DI1 = Z_DIX*DI1
      ZTERM_DI2 = Z_DIX*DI2
      CF1XOT1 = CF1*XOT1
      CF2XOT2 = CF2*XOT2
      DI1XOT1 = DI1*XOT1
      DI2XOT2 = DI2*XOT2
      ZT1_BODY = (1.0-UPW)*(ZTERM_CF1 + ZTERM_DI1)*(-XOT1/T1)
      ZT2_BODY =      UPW *(ZTERM_CF2 + ZTERM_DI2)*(-XOT2/T2)
      ZT1_WAKE = Z_HWA*0.5*(-DW1/T1**2)
      ZT2_WAKE = Z_HWA*0.5*(-DW2/T2**2)
C
      ROW32T1_BASEHS = Z_HS1*HS1_T1
      ROW32T1_BASECF = Z_CF1*CF1_T1
      ROW32T1_BASEDI = Z_DI1*DI1_T1
      ROW32T1_EXTRAH = 0.5*(Z_HCA*HC1_T1+Z_HA*H1_T1)
      ROW32T1_EXTRAUPW = Z_UPW*UPW_T1
      ROW32_BASEHS = Z_HS2*HS2_T2
      ROW32_BASECF = Z_CF2*CF2_T2
      ROW32_BASEDI = Z_DI2*DI2_T2
      ROW32_EXTRAH = 0.5*(Z_HCA*HC2_T2+Z_HA*H2_T2)
      ROW32_EXTRAUPW = Z_UPW*UPW_T2
      ROW32_BASESTORE = ROW32_BASEHS + ROW32_BASECF + ROW32_BASEDI
     &                + Z_T2
      ROW32_VAL = ROW32_BASESTORE + ROW32_EXTRAH + ROW32_EXTRAUPW
C
      ROW31_BASEHS = Z_HS1*HS1_D1
      ROW31_BASECF = Z_CF1*CF1_D1
      ROW31_BASEDI = Z_DI1*DI1_D1
      ROW31_EXTRAH = 0.5*(Z_HCA*HC1_D1+Z_HA*H1_D1)
      ROW31_EXTRAUPW = Z_UPW*UPW_D1
      ROW31_VAL = ROW31_BASEHS + ROW31_BASECF + ROW31_BASEDI
     &          + ROW31_EXTRAH + ROW31_EXTRAUPW
C
      ROW33_BASEHS = Z_HS2*HS2_D2
      ROW33_BASECF = Z_CF2*CF2_D2
      ROW33_BASEDI = Z_DI2*DI2_D2
      ROW33_EXTRAH = 0.5*(Z_HCA*HC2_D2+Z_HA*H2_D2)
      ROW33_EXTRAUPW = Z_UPW*UPW_D2
      ROW33_VAL = ROW33_BASEHS + ROW33_BASECF + ROW33_BASEDI
     &          + ROW33_EXTRAH + ROW33_EXTRAUPW
C
      VS1(3,1) =                               Z_DI1*DI1_S1
      VS1(3,2) = Z_HS1*HS1_T1 + Z_CF1*CF1_T1 + Z_DI1*DI1_T1 + Z_T1
      ROW32T1_BASESTORE = VS1(3,2)
      IF(TRACE_SIDE.EQ.2 .AND. (TRACE_STATION.EQ.71
     & .OR.TRACE_STATION.EQ.73.OR.TRACE_STATION.EQ.93)) THEN
       WRITE(*,'(A,I3,A,I1,1X,Z8,1X,Z8,1X,Z8,1X,Z8,1X,Z8,1X,Z8)')
     &  'F_DI1T i=',TRACE_STATION,' ty=',ITYP,
     &  TRANSFER(DI1_T1,1),TRANSFER(DI1,1),
     &  TRANSFER(HK1,1),TRANSFER(HS1,1),
     &  TRANSFER(US1,1),TRANSFER(RT1,1)
       WRITE(*,'(A,I3,A,I1,1X,Z8,1X,Z8,1X,Z8,1X,Z8,1X,Z8,1X,Z8)')
     &  'F_DI2T i=',TRACE_STATION,' ty=',ITYP,
     &  TRANSFER(DI2_T2,1),TRANSFER(DI2,1),
     &  TRANSFER(HK2,1),TRANSFER(HS2,1),
     &  TRANSFER(US2,1),TRANSFER(RT2,1)
      ENDIF
      VS1(3,3) = Z_HS1*HS1_D1 + Z_CF1*CF1_D1 + Z_DI1*DI1_D1
      VS1(3,4) = Z_HS1*HS1_U1 + Z_CF1*CF1_U1 + Z_DI1*DI1_U1 + Z_U1
      VS1(3,5) =                                              Z_X1
      VS2(3,1) =                               Z_DI2*DI2_S2
      VS2(3,2) = Z_HS2*HS2_T2 + Z_CF2*CF2_T2 + Z_DI2*DI2_T2 + Z_T2
      VS2(3,3) = Z_HS2*HS2_D2 + Z_CF2*CF2_D2 + Z_DI2*DI2_D2
      VS2(3,4) = Z_HS2*HS2_U2 + Z_CF2*CF2_U2 + Z_DI2*DI2_U2 + Z_U2
      VS2(3,5) =                                              Z_X2
      ROW31_BASESTORE = VS1(3,3)
      ROW33_BASESTORE = VS2(3,3)
      VSM(3)   = Z_HS1*HS1_MS + Z_CF1*CF1_MS + Z_DI1*DI1_MS
     &         + Z_HS2*HS2_MS + Z_CF2*CF2_MS + Z_DI2*DI2_MS
      VSR(3)   = Z_HS1*HS1_RE + Z_CF1*CF1_RE + Z_DI1*DI1_RE
     &         + Z_HS2*HS2_RE + Z_CF2*CF2_RE + Z_DI2*DI2_RE
C
      VS1(3,2) = VS1(3,2) + 0.5*(Z_HCA*HC1_T1+Z_HA*H1_T1) + Z_UPW*UPW_T1
      VS1(3,3) = VS1(3,3) + 0.5*(Z_HCA*HC1_D1+Z_HA*H1_D1) + Z_UPW*UPW_D1
      VS1(3,4) = VS1(3,4) + 0.5*(Z_HCA*HC1_U1           ) + Z_UPW*UPW_U1
      VS2(3,2) = VS2(3,2) + 0.5*(Z_HCA*HC2_T2+Z_HA*H2_T2) + Z_UPW*UPW_T2
      VS2(3,3) = VS2(3,3) + 0.5*(Z_HCA*HC2_D2+Z_HA*H2_D2) + Z_UPW*UPW_D2
      VS2(3,4) = VS2(3,4) + 0.5*(Z_HCA*HC2_U2           ) + Z_UPW*UPW_U2
C
      IF(TRACE_SIDE.EQ.1 .AND. TRACE_STATION.EQ.78
     &   .AND. TRACE_ITER.EQ.5) THEN
        WRITE(0,'(A,I1,A,I1,10(1X,Z8.8))')
     &    'F_VS3ROW_78 it=',TRACE_ITER,' ty=',ITYP,
     &    TRANSFER(REAL(VS1(3,1)),1), TRANSFER(REAL(VS1(3,2)),1),
     &    TRANSFER(REAL(VS1(3,3)),1), TRANSFER(REAL(VS1(3,4)),1),
     &    TRANSFER(REAL(VS1(3,5)),1),
     &    TRANSFER(REAL(VS2(3,1)),1), TRANSFER(REAL(VS2(3,2)),1),
     &    TRANSFER(REAL(VS2(3,3)),1), TRANSFER(REAL(VS2(3,4)),1),
     &    TRANSFER(REAL(VS2(3,5)),1)
        WRITE(0,'(A,I1,A,I1,10(1X,Z8.8))')
     &    'F_VS3IN_78 it=',TRACE_ITER,' ty=',ITYP,
     &    TRANSFER(REAL(HS1_T1),1),TRANSFER(REAL(HS1_D1),1),
     &    TRANSFER(REAL(HS1_U1),1),TRANSFER(REAL(HS2_T2),1),
     &    TRANSFER(REAL(HS2_D2),1),TRANSFER(REAL(HS2_U2),1),
     &    TRANSFER(REAL(CF1_T1),1),TRANSFER(REAL(CF2_T2),1),
     &    TRANSFER(REAL(DI1_T1),1),TRANSFER(REAL(DI2_T2),1)
        WRITE(0,'(A,I1,15(1X,Z8.8))')
     &    'F_VS3ZIN_78 it=',TRACE_ITER,
     &    TRANSFER(REAL(Z_HS1),1),TRANSFER(REAL(Z_HS2),1),
     &    TRANSFER(REAL(Z_CF1),1),TRANSFER(REAL(Z_CF2),1),
     &    TRANSFER(REAL(Z_DI1),1),TRANSFER(REAL(Z_DI2),1),
     &    TRANSFER(REAL(Z_T1),1),TRANSFER(REAL(Z_T2),1),
     &    TRANSFER(REAL(Z_U1),1),TRANSFER(REAL(Z_U2),1),
     &    TRANSFER(REAL(Z_X1),1),TRANSFER(REAL(Z_X2),1),
     &    TRANSFER(REAL(Z_UPW),1),TRANSFER(REAL(Z_HCA),1),
     &    TRANSFER(REAL(Z_HA),1)
      ENDIF
C
      CALL TRACE_BLDIF_EQ3_T1_TERMS('BLDIF',
     &    TRACE_SIDE, TRACE_STATION, ITYP,
     &    X1, X2, T1, T2, U1, U2, UPW, XOT1, XOT2,
     &    CF1, CF2, DI1, DI2,
     &    CF1XOT1, CF2XOT2, DI1XOT1, DI2XOT2,
     &    ZTERM_CF1, ZTERM_DI1, ZT1_BODY, ZT1_WAKE,
     &    Z_HS1, HS1_T1, Z_CF1, CF1_T1, Z_DI1, DI1_T1,
     &    ROW32T1_BASEHS, ROW32T1_BASECF, ROW32T1_BASEDI, Z_T1,
     &    ROW32T1_EXTRAH, Z_CFX, Z_DIX, CFX_UPW, DIX_UPW,
     &    Z_UPW, UPW_T1, ROW32T1_EXTRAUPW, ROW32T1_BASESTORE,
     &    VS1(3,2))
      CALL TRACE_BLDIF_EQ3_T2_TERMS('BLDIF',
     &    TRACE_SIDE, TRACE_STATION, ITYP,
     &    X1, X2, T1, T2, U1, U2, UPW, XOT1, XOT2,
     &    CF1, CF2, DI1, DI2,
     &    CF1XOT1, CF2XOT2, DI1XOT1, DI2XOT2,
     &    ZTERM_CF2, ZTERM_DI2, ZT2_BODY, ZT2_WAKE,
     &    Z_HS2, HS2_T2, Z_CF2, CF2_T2, Z_DI2, DI2_T2,
     &    ROW32_BASEHS, ROW32_BASECF, ROW32_BASEDI, Z_T2,
     &    ROW32_EXTRAH, Z_CFX, Z_DIX, CFX_UPW, DIX_UPW,
     &    Z_UPW, UPW_T2, ROW32_EXTRAUPW, ROW32_BASESTORE, VS2(3,2))
      CALL TRACE_BLDIF_EQ3_D1_TERMS('BLDIF', ITYP,
     &    Z_HS1, HS1_D1, Z_CF1, CF1_D1, Z_DI1, DI1_D1,
     &    ROW31_BASEHS, ROW31_BASECF, ROW31_BASEDI,
     &    ROW31_EXTRAH, XOT1, XOT2, CF1, CF2, DI1, DI2,
     &    Z_CFX, Z_DIX, CFX_UPW, DIX_UPW,
     &    Z_UPW, UPW_D1, ROW31_EXTRAUPW, ROW31_BASESTORE, VS1(3,3))
      CALL TRACE_BLDIF_EQ3_D2_TERMS('BLDIF', ITYP,
     &    ROW33_BASEHS, ROW33_BASECF, ROW33_BASEDI,
     &    ROW33_EXTRAH, XOT1, XOT2, CF1, CF2, DI1, DI2,
     &    Z_CFX, Z_DIX, CFX_UPW, DIX_UPW,
     &    Z_UPW, UPW_D2, ROW33_EXTRAUPW, ROW33_BASESTORE, VS2(3,3))
      CALL TRACE_BLDIF_EQ3_U2_TERMS('BLDIF', ITYP,
     &    Z_HS2, HS2_U2, Z_CF2, CF2_U2, Z_DI2, DI2_U2,
     &    Z_U2, 0.5*Z_HCA, HC2_U2, Z_UPW, UPW_U2,
     &    Z_HS2*HS2_U2, Z_CF2*CF2_U2, Z_DI2*DI2_U2, Z_U2,
     &    0.5*(Z_HCA*HC2_U2), Z_UPW*UPW_U2,
     &    Z_HS2*HS2_U2 + Z_CF2*CF2_U2 + Z_DI2*DI2_U2 + Z_U2,
     &    VS2(3,4))
C
      VSM(3)   = VSM(3)   + 0.5*(Z_HCA*HC1_MS           ) + Z_UPW*UPW_MS
     &                    + 0.5*(Z_HCA*HC2_MS           )
C
      VSX(3)   = 0.
      VSREZ(3) = -REZH
C
      CALL TRACE_BLDIF_RESIDUAL('BLDIF',
     &     TRACE_SIDE, TRACE_STATION, TRACE_PHASE, ITYP,
     &     VSREZ(1), VSREZ(2), VSREZ(3))
      CALL TRACE_EXIT('BLDIF')
      RETURN
      END


 
      SUBROUTINE DAMPL( HK, TH, RT, AX, AX_HK, AX_TH, AX_RT )
C==============================================================
C     Amplification rate routine for envelope e^n method.
C     Reference:
C                Drela, M., Giles, M.,
C               "Viscous/Inviscid Analysis of Transonic and
C                Low Reynolds Number Airfoils",
C                AIAA Journal, Oct. 1987.
C
C     NEW VERSION.   March 1991       (latest bug fix  July 93)
C          - m(H) correlation made more accurate up to H=20
C          - for H > 5, non-similar profiles are used 
C            instead of Falkner-Skan profiles.  These 
C            non-similar profiles have smaller reverse 
C            velocities, are more representative of typical 
C            separation bubble profiles.
C--------------------------------------------------------------
C
C     input :   HK     kinematic shape parameter
C               TH     momentum thickness
C               RT     momentum-thickness Reynolds number
C
C     output:   AX     envelope spatial amplification rate
C               AX_(.) sensitivity of AX to parameter (.)
C
C
C     Usage: The log of the envelope amplitude N(x) is
C            calculated by integrating AX (= dN/dx) with
C            respect to the streamwise distance x.
C                      x
C                     /
C              N(x) = | AX(H(x),Th(x),Rth(x)) dx
C                     /
C                      0
C            The integration can be started from the leading
C            edge since AX will be returned as zero when RT
C            is below the critical Rtheta.  Transition occurs
C            when N(x) reaches Ncrit (Ncrit= 9 is "standard").
C==============================================================
      IMPLICIT REAL (A-H,M,O-Z)
      INTEGER TRACE_SIDE, TRACE_STATION, TRACE_PHASE, TRACE_ITER
      INTEGER TRACE_OUTER
      COMMON/V_TRACE/ TRACE_SIDE, TRACE_STATION, TRACE_PHASE,
     &                 TRACE_ITER, TRACE_OUTER
ccc   DATA DGR / 0.04 /
      DATA DGR / 0.08 /
C
      HMI = 1.0/(HK - 1.0)
      HMI_HK = -HMI**2
C
C---- log10(Critical Rth) - H   correlation for Falkner-Skan profiles
      AA    = 2.492*HMI**0.43
      AA_HK =   (AA/HMI)*0.43 * HMI_HK
C
      BB    = TANH(14.0*HMI - 9.24)
      BB_HK = (1.0 - BB*BB) * 14.0 * HMI_HK
C
      GRCRIT = AA    + 0.7*(BB + 1.0)
      GRC_HK = AA_HK + 0.7* BB_HK
C
C
      GR = LOG10(RT)
      GR_RT = 1.0 / (2.3025851*RT)
C
      IF(GR .LT. GRCRIT-DGR) THEN
C
C----- no amplification for Rtheta < Rcrit
       AX    = 0.
       AX_HK = 0.
       AX_TH = 0.
       AX_RT = 0.
       CALL TRACE_TRANSITION_DAMPL_TERMS('DAMPL',
     &      HK, TH, RT, GR, GRCRIT, 0.0, 0.0, 0.0, AX,
     &      AX_HK, AX_TH, AX_RT)
C
      ELSE
C
C----- Set steep cubic ramp used to turn on AX smoothly as Rtheta 
C-     exceeds Rcrit (previously, this was done discontinuously).
C-     The ramp goes between  -DGR < log10(Rtheta/Rcrit) < DGR
C
       RNORM = (GR - (GRCRIT-DGR)) / (2.0*DGR)
       RN_HK =     -  GRC_HK       / (2.0*DGR)
       RN_RT =  GR_RT              / (2.0*DGR)
C
       IF(RNORM .GE. 1.0) THEN
        RFAC    = 1.0
        RFAC_HK = 0.
        RFAC_RT = 0.
       ELSE
        RFAC    = 3.0*RNORM**2 - 2.0*RNORM**3
        RFAC_RN = 6.0*RNORM    - 6.0*RNORM**2
C
        RFAC_HK = RFAC_RN*RN_HK
        RFAC_RT = RFAC_RN*RN_RT
       ENDIF
C
C----- Amplification envelope slope correlation for Falkner-Skan
       ARG    = 3.87*HMI    - 2.52
       ARG_HK = 3.87*HMI_HK
C
       EX    = EXP(-ARG**2)
       EX_HK = EX * (-2.0*ARG*ARG_HK)
C
       DADR    = 0.028*(HK-1.0) - 0.0345*EX
       DADR_HK = 0.028          - 0.0345*EX_HK
C
C----- new m(H) correlation    1 March 91
      AF = -0.05 + 2.7*HMI -  5.5*HMI**2 + 3.0*HMI**3
      AF_HMI =     2.7     - 11.0*HMI    + 9.0*HMI**2
      AF_HK = AF_HMI*HMI_HK
      CALL TRACE_TRANSITION_DAMPL_POLY_TERMS('DAMPL',
     &      HK, HMI, HMI**2, HMI**3,
     &      -0.05, 2.7*HMI, 5.5*HMI**2, 3.0*HMI**3, AF)
C
       AX    = (AF   *DADR/TH                ) * RFAC
       AX_HK = (AF_HK*DADR/TH + AF*DADR_HK/TH) * RFAC
     &       + (AF   *DADR/TH                ) * RFAC_HK
       AX_TH = -AX/TH
       AX_RT = (AF   *DADR/TH                ) * RFAC_RT
       IF(TRACE_SIDE.EQ.1 .AND. TRACE_STATION.GE.24
     &    .AND. TRACE_STATION.LE.26
     &    .AND. (TRACE_OUTER.LE.1 .OR. TRACE_OUTER.EQ.8)) THEN
        WRITE(0,'(A,I3,A,Z8,A,Z8,A,Z8,A,Z8,A,Z8,A,Z8)')
     &   'F_DAMPL i=',TRACE_STATION,
     &   ' HMI=',TRANSFER(HMI,1),
     &   ' AA=',TRANSFER(AA,1),
     &   ' BB=',TRANSFER(BB,1),
     &   ' GRC=',TRANSFER(GRCRIT,1),
     &   ' GR=',TRANSFER(GR,1),
     &   ' RFAC=',TRANSFER(RFAC,1)
        WRITE(0,'(A,I3,A,Z8,A,Z8,A,Z8,A,Z8)')
     &   'F_DAMPL2 i=',TRACE_STATION,
     &   ' EX=',TRANSFER(EX,1),
     &   ' DADR=',TRANSFER(DADR,1),
     &   ' AF=',TRANSFER(AF,1),
     &   ' AX=',TRANSFER(AX,1)
       ENDIF
       CALL TRACE_TRANSITION_DAMPL_DERIVATIVE_TERMS('DAMPL',
     &      HK, HMI, HMI**2, HMI_HK, BB, 1.0-BB*BB,
     &      AA_HK, BB_HK, GRC_HK, GR_RT,
     &      RN_HK, RN_RT, RFAC_HK, RFAC_RT,
     &      ARG_HK, EX_HK, DADR_HK, AF_HMI, AF_HK,
     &      AF*DADR/TH, AF_HK*DADR/TH + AF*DADR_HK/TH,
     &      AX_HK, AX_RT)
       CALL TRACE_TRANSITION_DAMPL_TERMS('DAMPL',
     &      HK, TH, RT, GR, GRCRIT, RFAC, DADR, AF, AX,
     &      AX_HK, AX_TH, AX_RT)
C
      ENDIF
C
      RETURN
      END ! DAMPL


 
      SUBROUTINE DAMPL2( HK, TH, RT, AX, AX_HK, AX_TH, AX_RT )
C==============================================================
C     Amplification rate routine for modified envelope e^n method.
C     Reference: 
C                Drela, M., Giles, M.,
C               "Viscous/Inviscid Analysis of Transonic and 
C                Low Reynolds Number Airfoils", 
C                AIAA Journal, Oct. 1987.
C
C     NEWER VERSION.   Nov 1996
C          - Amplification rate changes to the Orr-Sommerfeld
C              maximum ai(H,Rt) function for H > 4 .
C          - This implicitly assumes that the frequency range
C              (around w = 0.09 Ue/theta) which experiences this 
C              maximum amplification rate contains the currently
C              most-amplified frequency.
C--------------------------------------------------------------
C
C     input :   HK     kinematic shape parameter
C               TH     momentum thickness
C               RT     momentum-thickness Reynolds number
C
C     output:   AX     envelope spatial amplification rate
C               AX_(.) sensitivity of AX to parameter (.)
C
C
C     Usage: The log of the envelope amplitude N(x) is 
C            calculated by integrating AX (= dN/dx) with 
C            respect to the streamwise distance x.
C                      x
C                     /
C              N(x) = | AX(H(x),Th(x),Rth(x)) dx
C                     /
C                      0
C            The integration can be started from the leading
C            edge since AX will be returned as zero when RT
C            is below the critical Rtheta.  Transition occurs
C            when N(x) reaches Ncrit (Ncrit= 9 is "standard").
C==============================================================
      IMPLICIT REAL (A-H,M,O-Z)
      DATA DGR / 0.08 /
      DATA HK1, HK2 / 3.5, 4.0 /
C
      HMI = 1.0/(HK - 1.0)
      HMI_HK = -HMI**2
C
C---- log10(Critical Rth) -- H   correlation for Falkner-Skan profiles
      AA    = 2.492*HMI**0.43
      AA_HK =   (AA/HMI)*0.43 * HMI_HK
C
      BB    = TANH(14.0*HMI - 9.24)
      BB_HK = (1.0 - BB*BB) * 14.0 * HMI_HK
C
      GRC = AA    + 0.7*(BB + 1.0)
      GRC_HK = AA_HK + 0.7* BB_HK
C
C
      GR = LOG10(RT)
      GR_RT = 1.0 / (2.3025851*RT)
C
      IF(GR .LT. GRC-DGR) THEN
C
C----- no amplification for Rtheta < Rcrit
       AX    = 0.
       AX_HK = 0.
       AX_TH = 0.
       AX_RT = 0.
C
      ELSE
C
C----- Set steep cubic ramp used to turn on AX smoothly as Rtheta 
C-     exceeds Rcrit (previously, this was done discontinuously).
C-     The ramp goes between  -DGR < log10(Rtheta/Rcrit) < DGR
C
       RNORM = (GR - (GRC-DGR)) / (2.0*DGR)
       RN_HK =     -  GRC_HK       / (2.0*DGR)
       RN_RT =  GR_RT              / (2.0*DGR)
C
       IF(RNORM .GE. 1.0) THEN
        RFAC    = 1.0
        RFAC_HK = 0.
        RFAC_RT = 0.
       ELSE
        RFAC    = 3.0*RNORM**2 - 2.0*RNORM**3
        RFAC_RN = 6.0*RNORM    - 6.0*RNORM**2
C
        RFAC_HK = RFAC_RN*RN_HK
        RFAC_RT = RFAC_RN*RN_RT
       ENDIF
C
C
C----- set envelope amplification rate with respect to Rtheta
C-       DADR = d(N)/d(Rtheta) = f(H)
C
       ARG    = 3.87*HMI    - 2.52
       ARG_HK = 3.87*HMI_HK
C
       EX    = EXP(-ARG**2)
       EX_HK = EX * (-2.0*ARG*ARG_HK)
C
       DADR    = 0.028*(HK-1.0) - 0.0345*EX
       DADR_HK = 0.028          - 0.0345*EX_HK
C
C
C----- set conversion factor from d/d(Rtheta) to d/dx
C-       AF = Theta d(Rtheta)/dx = f(H)
C
       BRG = -20.0*HMI
       AF = -0.05 + 2.7*HMI -  5.5*HMI**2 + 3.0*HMI**3 + 0.1*EXP(BRG)
       AF_HMI =     2.7     - 11.0*HMI    + 9.0*HMI**2 - 2.0*EXP(BRG)
       AF_HK = AF_HMI*HMI_HK
C
C
C----- set amplification rate with respect to x, 
C-     with RFAC shutting off amplification when below Rcrit
C
       AX    = (AF   *DADR/TH                ) * RFAC
       AX_HK = (AF_HK*DADR/TH + AF*DADR_HK/TH) * RFAC
     &       + (AF   *DADR/TH                ) * RFAC_HK
       AX_TH = -AX/TH
       AX_RT = (AF   *DADR/TH                ) * RFAC_RT
C
      ENDIF
C
      IF(HK .LT. HK1) RETURN
C
C---- non-envelope max-amplification correction for separated profiles
C
      HNORM = (HK - HK1) / (HK2 - HK1)
      HN_HK =       1.0  / (HK2 - HK1)
C
C---- set blending fraction HFAC = 0..1 over HK1 < HK < HK2
      IF(HNORM .GE. 1.0) THEN
       HFAC = 1.0
       HF_HK = 0.
      ELSE
       HFAC  =  3.0*HNORM**2 - 2.0*HNORM**3
       HF_HK = (6.0*HNORM    - 6.0*HNORM**2)*HN_HK
      ENDIF
C
C---- "normal" envelope amplification rate AX1
      AX1    = AX
      AX1_HK = AX_HK
      AX1_TH = AX_TH
      AX1_RT = AX_RT
C
C---- set modified amplification rate AX2
      GR0 = 0.30 + 0.35 * EXP(-0.15*(HK-5.0))
      GR0_HK =   - 0.35 * EXP(-0.15*(HK-5.0)) * 0.15
C
      TNR = TANH(1.2*(GR - GR0))
      TNR_RT =  (1.0 - TNR**2)*1.2*GR_RT
      TNR_HK = -(1.0 - TNR**2)*1.2*GR0_HK
C
      AX2    = (0.086*TNR    -     0.25/(HK-1.0)**1.5) / TH
      AX2_HK = (0.086*TNR_HK + 1.5*0.25/(HK-1.0)**2.5) / TH
      AX2_RT = (0.086*TNR_RT                         ) / TH
      AX2_TH = -AX2/TH
C
      IF(AX2 .LT. 0.0) THEN
       AX2    = 0.0
       AX2_HK = 0.
       AX2_RT = 0.
       AX2_TH = 0.
      ENDIF
C
C---- blend the two amplification rates
      AX    = HFAC*AX2    + (1.0 - HFAC)*AX1
      AX_HK = HFAC*AX2_HK + (1.0 - HFAC)*AX1_HK + HF_HK*(AX2-AX1)
      AX_RT = HFAC*AX2_RT + (1.0 - HFAC)*AX1_RT
      AX_TH = HFAC*AX2_TH + (1.0 - HFAC)*AX1_TH
C
      RETURN
      END ! DAMPL2

 
 
      SUBROUTINE HKIN( H, MSQ, HK, HK_H, HK_MSQ )
      REAL MSQ
C
C---- calculate kinematic shape parameter (assuming air)
C     (from Whitfield )
      HK     =    (H - 0.29*MSQ)/(1.0 + 0.113*MSQ)
      HK_H   =     1.0          /(1.0 + 0.113*MSQ)
      HK_MSQ = (-.29 - 0.113*HK)/(1.0 + 0.113*MSQ)
C
      RETURN
      END
 


      SUBROUTINE DIL( HK, RT, DI, DI_HK, DI_RT )
C
C---- Laminar dissipation function  ( 2 CD/H* )     (from Falkner-Skan)
      REAL HKB, HKBSQ, DEN, RATIO, NUMER
      HKB = 0.0
      HKBSQ = 0.0
      DEN = 0.0
      RATIO = 0.0
      NUMER = 0.0
      IF(HK.LT.4.0) THEN
       NUMER = 0.00205  *  (4.0-HK)**5.5 + 0.207
       DI    = NUMER / RT
       DI_HK = ( -.00205*5.5*(4.0-HK)**4.5         ) / RT
      ELSE
       HKB = HK - 4.0
       HKBSQ = HKB**2
       DEN = 1.0 + 0.02*HKBSQ
       RATIO = HKBSQ/DEN
       NUMER = -.0016*RATIO + 0.207
       DI    = NUMER / RT
       DI_HK = ( -.0016*2.0*HKB*(1.0/DEN - 0.02*HKB**2/DEN**2) ) / RT
      ENDIF
      DI_RT = -DI/RT
C
      CALL TRACE_LAMINAR_DISSIPATION('DIL', HK, RT,
     &    HKB, HKBSQ, DEN, RATIO, NUMER, DI, DI_HK, DI_RT)
C
      RETURN
      END


      SUBROUTINE DILW( HK, RT, DI, DI_HK, DI_RT )
      REAL MSQ
C
      MSQ = 0.
      CALL HSL( HK, RT, MSQ, HS, HS_HK, HS_RT, HS_MSQ )
C
C---- Laminar wake dissipation function  ( 2 CD/H* )
      RCD    =  1.10 * (1.0 - 1.0/HK)**2  / HK
      RCD_HK = -1.10 * (1.0 - 1.0/HK)*2.0 / HK**3
     &       - RCD/HK
C
      DI    = 2.0*RCD   /(HS*RT)
      DI_HK = 2.0*RCD_HK/(HS*RT) - (DI/HS)*HS_HK
      DI_RT = -DI/RT             - (DI/HS)*HS_RT
C
      RETURN
      END


      SUBROUTINE HSL( HK, RT, MSQ, HS, HS_HK, HS_RT, HS_MSQ )
      REAL MSQ
      REAL TMP2, TMP3, HKP1, HSHK1, HSHK2, HSHK3
C
C---- Laminar HS correlation
      IF(HK.LT.4.35) THEN
       TMP = HK - 4.35
       TMP2 = TMP**2
       TMP3 = TMP**3
       HKP1 = HK + 1.0
       HS    = 0.0111*TMP**2/(HK+1.0)
     &       - 0.0278*TMP**3/(HK+1.0)  + 1.528
     &       - 0.0002*(TMP*HK)**2
       HS_HK = 0.0111*(2.0*TMP    - TMP**2/(HK+1.0))/(HK+1.0)
     &       - 0.0278*(3.0*TMP**2 - TMP**3/(HK+1.0))/(HK+1.0)
     &       - 0.0002*2.0*TMP*HK * (TMP + HK)
       HSHK1 = 0.0111*(2.0*TMP - TMP2/HKP1)/HKP1
       HSHK2 =-0.0278*(3.0*TMP2 - TMP3/HKP1)/HKP1
       HSHK3 =-0.0002*2.0*TMP*HK*(TMP + HK)
      ELSE
       HS    = 0.015*    (HK-4.35)**2/HK + 1.528
       HS_HK = 0.015*2.0*(HK-4.35)   /HK
     &       - 0.015*    (HK-4.35)**2/HK**2
       TMP = 0.0
       TMP2 = 0.0
       TMP3 = 0.0
       HKP1 = 0.0
       HSHK1 = 0.0
       HSHK2 = 0.0
       HSHK3 = 0.0
      ENDIF
C
      CALL TRACE_HSL_TERMS('HSL', HK, HS, HS_HK,
     &                     TMP, HKP1, HSHK1, HSHK2, HSHK3)
C
      HS_RT  = 0.
      HS_MSQ = 0.
C
      RETURN
      END


      SUBROUTINE CFL( HK, RT, MSQ, CF, CF_HK, CF_RT, CF_MSQ )
      REAL MSQ
C
C---- Laminar skin friction function  ( Cf )    ( from Falkner-Skan )
      IF(HK.LT.5.5) THEN
       TMP = (5.5-HK)**3 / (HK+1.0)
       CF    = ( 0.0727*TMP                      - 0.07       )/RT
       CF_HK = ( -.0727*TMP*3.0/(5.5-HK) - 0.0727*TMP/(HK+1.0))/RT
      ELSE
       TMP = 1.0 - 1.0/(HK-4.5)
       CF    = ( 0.015*TMP**2      - 0.07  ) / RT
       CF_HK = ( 0.015*TMP*2.0/(HK-4.5)**2 ) / RT
      ENDIF
      CF_RT = -CF/RT
      CF_MSQ = 0.0
C
      RETURN
      END



      SUBROUTINE DIT( HS, US, CF, ST, DI, DI_HS, DI_US, DI_CF, DI_ST )
C
C---- Turbulent dissipation function  ( 2 CD/H* )
      DI    =  ( 0.5*CF*US + ST*ST*(1.0-US) ) * 2.0/HS
      DI_HS = -( 0.5*CF*US + ST*ST*(1.0-US) ) * 2.0/HS**2
      DI_US =  ( 0.5*CF    - ST*ST          ) * 2.0/HS
      DI_CF =  ( 0.5   *US                  ) * 2.0/HS
      DI_ST =  (            2.0*ST*(1.0-US) ) * 2.0/HS
C
      RETURN
      END


      SUBROUTINE HST( HK, RT, MSQ, HS, HS_HK, HS_RT, HS_MSQ )
      IMPLICIT REAL (A-H,M,O-Z)
C
C---- Turbulent HS correlation
C
      DATA HSMIN, DHSINF / 1.500, 0.015 /
C
C---- ###  12/4/94
C---- limited Rtheta dependence for Rtheta < 200
C
C
      BRANCH = 0.
      GRT = 0.
      HDIF = 0.
      RTMP = 0.
      HTMP = 0.
      HTMP_HK = 0.
      HTMP_RT = 0.
      HS_HK_TERM1 = 0.
      HS_HK_TERM2 = 0.
      HS_RT_RAW = 0.
      HS_RT_TERM1 = 0.
      HS_RT_TERM2 = 0.
      HS_RT_TERM3 = 0.

      IF(RT.GT.400.0) THEN
       HO    = 3.0 + 400.0/RT
       HO_RT =     - 400.0/RT**2
      ELSE
       HO    = 4.0
       HO_RT = 0.
      ENDIF
C
      IF(RT.GT.200.0) THEN
       RTZ    = RT
       RTZ_RT = 1.
      ELSE
       RTZ    = 200.0
       RTZ_RT = 0.
      ENDIF
C
      IF(HK.LT.HO) THEN
       BRANCH = 1.
C----- attached branch
C=======================================================
C----- old correlation
C-     (from Swafford profiles)
c       SRT = SQRT(RT)
c       HEX = (HO-HK)**1.6
c       RTMP = 0.165 - 1.6/SRT
c       HS    = HSMIN + 4.0/RT + RTMP*HEX/HK
c       HS_HK = RTMP*HEX/HK*(-1.6/(HO-HK) - 1.0/HK)
c       HS_RT = -4.0/RT**2 + HEX/HK*0.8/SRT/RT
c     &             + RTMP*HEX/HK*1.6/(HO-HK)*HO_RT
C=======================================================
C----- new correlation  29 Nov 91
C-     (from  arctan(y+) + Schlichting  profiles)
       HR    = ( HO - HK)/(HO-1.0)
       HR_HK =      - 1.0/(HO-1.0)
       HR_RT = (1.0 - HR)/(HO-1.0) * HO_RT
       HS    = (2.0-HSMIN-4.0/RTZ)*HR**2  * 1.5/(HK+0.5) + HSMIN
     &       + 4.0/RTZ
       HS_HK =-(2.0-HSMIN-4.0/RTZ)*HR**2  * 1.5/(HK+0.5)**2
     &       + (2.0-HSMIN-4.0/RTZ)*HR*2.0 * 1.5/(HK+0.5) * HR_HK
       HS_RT = (2.0-HSMIN-4.0/RTZ)*HR*2.0 * 1.5/(HK+0.5) * HR_RT
     &       + (HR**2 * 1.5/(HK+0.5) - 1.0)*4.0/RTZ**2 * RTZ_RT
       HS_RT_RAW = HS_RT
C
      ELSE
       BRANCH = 2.
C
C----- separated branch
       GRT = LOG(RTZ)
       HDIF = HK - HO 
       RTMP = HK - HO + 4.0/GRT
       HTMP    = 0.007*GRT/RTMP**2 + DHSINF/HK
       HTMP_HK = -.014*GRT/RTMP**3 - DHSINF/HK**2
       HTMP_RT = -.014*GRT/RTMP**3 * (-HO_RT - 4.0/GRT**2/RTZ * RTZ_RT)
     &         + 0.007    /RTMP**2 / RTZ * RTZ_RT
       HS    = HDIF**2 * HTMP + HSMIN + 4.0/RTZ
       HS_HK = HDIF*2.0* HTMP
     &       + HDIF**2 * HTMP_HK
       HS_HK_TERM1 = HDIF*2.0* HTMP
       HS_HK_TERM2 = HDIF**2 * HTMP_HK
       HS_RT_TERM1 = HDIF**2 * HTMP_RT
       HS_RT_TERM2 = - 4.0/RTZ**2 * RTZ_RT
       HS_RT_TERM3 = HDIF*2.0* HTMP * (-HO_RT)
       HS_RT = HS_RT_TERM1 + HS_RT_TERM2 + HS_RT_TERM3
       HS_RT_RAW = HS_RT
C
      ENDIF
C
C---- fudge HS slightly to make sure   HS -> 2   as   HK -> 1
C-    (unnecessary with new correlation)
c      HTF    = 0.485/9.0 * (HK-4.0)**2/HK  +  1.515
c      HTF_HK = 0.485/9.0 * (1.0-16.0/HK**2)
c      ARG = MAX( 10.0*(1.0 - HK) , -15.0 )
c      HXX = EXP(ARG)
c      HXX_HK = -10.0*HXX
cC
c      HS_HK  = (1.0-HXX)*HS_HK  +  HXX*HTF_HK
c     &       + (        -HS     +      HTF    )*HXX_HK
c      HS_RT  = (1.0-HXX)*HS_RT
c      HS     = (1.0-HXX)*HS     +  HXX*HTF
C
C---- Whitfield's minor additional compressibility correction
      FM = 1.0 + 0.014*MSQ
      HS     = ( HS + 0.028*MSQ ) / FM
      HS_HK  = ( HS_HK          ) / FM
      HS_RT  = ( HS_RT          ) / FM
      HS_MSQ = 0.028/FM  -  0.014*HS/FM
      CALL TRACE_HST_TERMS('HST', HK, RT, MSQ,
     & BRANCH, HO, HO_RT, RTZ, RTZ_RT,
     & GRT, HDIF, RTMP, HTMP, HTMP_HK, HTMP_RT,
     & HS_HK_TERM1, HS_HK_TERM2, HS_RT_RAW,
     & HS_RT_TERM1, HS_RT_TERM2, HS_RT_TERM3,
     & HS, HS_HK, HS_RT, HS_MSQ, FM)
C
      RETURN
      END
 
 
 
      SUBROUTINE CFT( HK, RT, MSQ, CF, CF_HK, CF_RT, CF_MSQ )
      IMPLICIT REAL (A-H,M,O-Z)
      INCLUDE 'BLPAR.INC'
C
      DATA GAM /1.4/
C
C---- Turbulent skin friction function  ( Cf )    (Coles)
      GM1 = GAM - 1.0
      FC = SQRT(1.0 + 0.5*GM1*MSQ)
      GRT = LOG(RT/FC)
      GRT = MAX(GRT,3.0)
C
      GEX = -1.74 - 0.31*HK
C
      ARG = -1.33*HK
      ARG = MAX(-20.0, ARG )
C
      THK = TANH(4.0 - HK/0.875)
C
      CFO =  CFFAC * 0.3*EXP(ARG) * (GRT/2.3026)**GEX
      CF     = ( CFO  +  1.1E-4*(THK-1.0) ) / FC
      CFHKTERM1 = -1.33*CFO
      CFHKTERM2 = -0.31*LOG(GRT/2.3026)*CFO
      CFHKTERM3 = -1.1E-4*(1.0-THK**2) / 0.875
      THKSQ = THK**2
      ONEMTHKSQ = 1.0 - THKSQ
      SCALEDTHKDIFF = -1.1E-4*ONEMTHKSQ
      CF_HK  = (CFHKTERM1 + CFHKTERM2 + CFHKTERM3) / FC
      CF_RT  = GEX*CFO/(FC*GRT) / RT
      CF_MSQ = GEX*CFO/(FC*GRT) * (-0.25*GM1/FC**2) - 0.25*GM1*CF/FC**2
C
      CALL TRACE_CFT_TERMS('CFT', HK, RT, MSQ,
     &     FC, GRT, GEX, ARG, THK, THKSQ, ONEMTHKSQ, SCALEDTHKDIFF, CFO,
     &     CFHKTERM1, CFHKTERM2, CFHKTERM3,
     &     CF, CF_HK, CF_RT, CF_MSQ)
C
      RETURN
      END ! CFT


 
      SUBROUTINE HCT( HK, MSQ, HC, HC_HK, HC_MSQ )
      REAL MSQ
C
C---- density shape parameter    (from Whitfield)
      HC     = MSQ * (0.064/(HK-0.8) + 0.251)
      HC_HK  = MSQ * (-.064/(HK-0.8)**2     )
      HC_MSQ =        0.064/(HK-0.8) + 0.251
C
      RETURN
      END
 
 
