C***********************************************************************
C    Module:  xbl.f
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
C
      SUBROUTINE SETBL
C-------------------------------------------------
C     Sets up the BL Newton system coefficients
C     for the current BL variables and the edge
C     velocities received from SETUP. The local
C     BL system coefficients are then
C     incorporated into the global Newton system.  
C-------------------------------------------------
      INCLUDE 'XFOIL.INC'
      INCLUDE 'XBL.INC'
      REAL USAV(IVX,2)
      REAL U1_M(2*IVX), U2_M(2*IVX)
      REAL D1_M(2*IVX), D2_M(2*IVX)
      REAL ULE1_M(2*IVX), ULE2_M(2*IVX)
      REAL UTE1_M(2*IVX), UTE2_M(2*IVX)
      REAL MA_CLMR, MSQ_CLMR, MDI
      INTEGER SETBL_COUNT
      INTEGER MDU_T_HASH, MDU_IS, MDU_IBL, IHS, IHI, HTH, HDH, HUH, IXX
      SAVE SETBL_COUNT
      DATA SETBL_COUNT /0/
      SETBL_COUNT = SETBL_COUNT + 1
C
C---- set the CL used to define Mach, Reynolds numbers
      IF(LALFA) THEN
       CLMR = CL
      ELSE
       CLMR = CLSPEC
      ENDIF
C
C---- set current MINF(CL)
      CALL MRCL(CLMR,MA_CLMR,RE_CLMR)
      MSQ_CLMR = 2.0*MINF*MA_CLMR
C
C---- set compressibility parameter TKLAM and derivative TK_MSQ
      CALL COMSET
C
C---- set gas constant (= Cp/Cv)
      GAMBL = GAMMA
      GM1BL = GAMM1
C
C---- set parameters for compressibility correction
      QINFBL = QINF
      TKBL    = TKLAM
      TKBL_MS = TKL_MSQ
C
C---- stagnation density and 1/enthalpy
      RSTBL    = (1.0 + 0.5*GM1BL*MINF**2) ** (1.0/GM1BL)
      RSTBL_MS = 0.5*RSTBL/(1.0 + 0.5*GM1BL*MINF**2)
C
      HSTINV    = GM1BL*(MINF/QINFBL)**2 / (1.0 + 0.5*GM1BL*MINF**2)
      HSTINV_MS = GM1BL*( 1.0/QINFBL)**2 / (1.0 + 0.5*GM1BL*MINF**2)
     &                - 0.5*GM1BL*HSTINV / (1.0 + 0.5*GM1BL*MINF**2)
C
C---- set Reynolds number based on freestream density, velocity, viscosity
      HERAT    = 1.0 - 0.5*QINFBL**2*HSTINV
      HERAT_MS =     - 0.5*QINFBL**2*HSTINV_MS
C
      REYBL    = REINF * SQRT(HERAT**3) * (1.0+HVRAT)/(HERAT+HVRAT)
      REYBL_RE =         SQRT(HERAT**3) * (1.0+HVRAT)/(HERAT+HVRAT)
      REYBL_MS = REYBL * (1.5/HERAT - 1.0/(HERAT+HVRAT))*HERAT_MS
      CALL TRACE_COMPRESSIBILITY_PARAMETERS('COMSET', TKBL, QINFBL,
     &     TKBL_MS, HSTINV, HSTINV_MS, RSTBL, RSTBL_MS,
     &     REYBL, REYBL_RE, REYBL_MS)
      IF(SETBL_COUNT.GE.21 .AND. SETBL_COUNT.LE.23) THEN
        WRITE(0,'(A,I3,5(A,Z8))') 'F_COMP mc=',SETBL_COUNT,
     &   ' TKBL=',TRANSFER(TKBL,1),' QINFBL=',TRANSFER(QINFBL,1),
     &   ' MINF=',TRANSFER(MINF,1),' REINF=',TRANSFER(REINF,1),
     &   ' REYBL=',TRANSFER(REYBL,1)
      ENDIF
C
      AMCRIT = ACRIT
      IDAMPV = IDAMP
C
C---- save TE thickness
      DWTE = WGAP(1)
C
      IF(.NOT.LBLINI) THEN
C----- initialize BL by marching with Ue (fudge at separation)
       WRITE(*,*)
       WRITE(*,*) 'Initializing BL ...'
       CALL MRCHUE
       WRITE(*,'(A,I4,A,I4,A,I4,A,I4,A,I4,A,I4)')
     &  'F_INIT IBLTE1=',IBLTE(1),
     &  ' IBLTE2=',IBLTE(2),
     &  ' NBL1=',NBL(1),' NBL2=',NBL(2),
     &  ' ITRAN1=',ITRAN(1),' ITRAN2=',ITRAN(2)
       DO 897 IS=1, 2
         DO 896 IBL=2, NBL(IS)
           WRITE(*,'(A,I1,A,I4,A,Z8,A,Z8,A,Z8)')
     &       'F_MUE s=',IS,' i=',IBL,
     &       ' T=',TRANSFER(THET(IBL,IS),1),
     &       ' D=',TRANSFER(DSTR(IBL,IS),1),
     &       ' C=',TRANSFER(CTAU(IBL,IS),1)
 896     CONTINUE
 897   CONTINUE
       WRITE(*,'(A,Z8,A,Z8,A,Z8,A,Z8)')
     &  'F_POST_MRCHUE s1_57 T=',TRANSFER(THET(57,1),1),
     &  ' D=',TRANSFER(DSTR(57,1),1),
     &  ' U=',TRANSFER(UEDG(57,1),1),
     &  ' C=',TRANSFER(CTAU(57,1),1)
       LBLINI = .TRUE.
      ENDIF
C
      WRITE(*,*)
C
C---- pre-MRCHDU state dump at iterations 2..6
      IF(TRACE_OUTER .GE. 2 .AND. TRACE_OUTER .LE. 6) THEN
        DO 893 IS=1, 2
          DO 894 IBL=2, NBL(IS)
            WRITE(0,'(A,I1,A,I1,A,I4,A,Z8,A,Z8,A,Z8)')
     &        'F_PRE_MDU', TRACE_OUTER-1, ' s=', IS, ' i=', IBL,
     &        ' T=', TRANSFER(THET(IBL,IS),1),
     &        ' D=', TRANSFER(DSTR(IBL,IS),1),
     &        ' U=', TRANSFER(UEDG(IBL,IS),1)
 894      CONTINUE
 893    CONTINUE
      ENDIF
C---- pre-MRCHDU input hash
      IF(SETBL_COUNT.GE.9 .AND. SETBL_COUNT.LE.11) THEN
        MDU_T_HASH = 0
        MDU_IBL = 0
        MDU_IS = 0
        HTH = 0
        HDH = 0
        HUH = 0
        DO 8894 IHS=1, 2
          DO 8895 IHI=2, NBL(IHS)
            MDU_T_HASH = IEOR(MDU_T_HASH,TRANSFER(XSSI(IHI,IHS),1))
            MDU_IBL = IEOR(MDU_IBL,TRANSFER(CTAU(IHI,IHS),1))
            MDU_IS = IEOR(MDU_IS,TRANSFER(MASS(IHI,IHS),1))
            HTH = IEOR(HTH,TRANSFER(THET(IHI,IHS),1))
            HDH = IEOR(HDH,TRANSFER(DSTR(IHI,IHS),1))
            HUH = IEOR(HUH,TRANSFER(UEDG(IHI,IHS),1))
 8895     CONTINUE
 8894   CONTINUE
        WRITE(0,'(A,I3,6(A,Z8))')
     &   'F_MDU_IN_HASH mc=',SETBL_COUNT,
     &   ' X=',MDU_T_HASH,' C=',MDU_IBL,' M=',MDU_IS,
     &   ' T=',HTH,' D=',HDH,' U=',HUH
        IF(SETBL_COUNT.EQ.10) THEN
          DO IXX=65, 73
            WRITE(0,'(A,I3,A,Z8)')
     &       'F_MDU_IN_CTAU mc=10 i=',IXX,
     &       ' C=',TRANSFER(CTAU(IXX,2),1)
          ENDDO
        ENDIF
      ENDIF
C---- march BL with current Ue and Ds to establish transition
      CALL MRCHDU
      IF(TRACE_OUTER.EQ.32 .OR. TRACE_OUTER.EQ.33) THEN
        DO 7701 IAH=1, 2
          DO 7702 IAHB=2, NBL(IAH)
            WRITE(0,'(A,I3,A,I1,A,I4,A,Z8,A,Z8,A,Z8,A,Z8)')
     &       'F_POSTMDU_FULL it=',TRACE_OUTER,
     &       ' s=',IAH,' i=',IAHB,
     &       ' T=',TRANSFER(THET(IAHB,IAH),1),
     &       ' D=',TRANSFER(DSTR(IAHB,IAH),1),
     &       ' U=',TRANSFER(UEDG(IAHB,IAH),1),
     &       ' C=',TRANSFER(CTAU(IAHB,IAH),1)
 7702     CONTINUE
 7701   CONTINUE
      ENDIF
      WRITE(0,'(A,I3,A,I4,A,I4)')
     & 'F_ITRAN_END sc=', SETBL_COUNT, ' s1=', ITRAN(1),
     & ' s2=', ITRAN(2)
      MDU_T_HASH = 0
      DO 8892 MDU_IS=1, 2
        DO 8893 MDU_IBL=1, NBL(MDU_IS)
          MDU_T_HASH = IEOR(MDU_T_HASH, TRANSFER(THET(MDU_IBL,MDU_IS),1))
 8893   CONTINUE
 8892 CONTINUE
      WRITE(0,'(A,I3,A,Z8)') 'F_MDU_HASH call=',SETBL_COUNT,
     & ' T_hash=',MDU_T_HASH
      IF(SETBL_COUNT.GE.21 .AND. SETBL_COUNT.LE.23) THEN
        DO 8896 IHS=1, 2
          MDU_T_HASH = 0
          DO 8897 IHI=2, NBL(IHS)
            MDU_T_HASH=IEOR(MDU_T_HASH,TRANSFER(THET(IHI,IHS),1))
 8897     CONTINUE
          WRITE(0,'(A,I3,A,I1,A,Z8)') 'F_MDU_OUT_SIDE mc=',SETBL_COUNT,
     &     ' s=',IHS,' T=',MDU_T_HASH
 8896   CONTINUE
      ENDIF
      IF(SETBL_COUNT.EQ.22) THEN
        DO 8898 IHI=2, NBL(2)
          WRITE(0,'(A,I3,A,Z8,A,Z8,A,Z8)') 'F_MDU22_S2 ibl=',IHI,
     &     ' T=',TRANSFER(THET(IHI,2),1),
     &     ' D=',TRANSFER(DSTR(IHI,2),1),
     &     ' U=',TRANSFER(UEDG(IHI,2),1)
 8898   CONTINUE
      ENDIF
      IF(SETBL_COUNT.EQ.3) THEN
        WRITE(0,'(A,I4,A,I4,A,I4,A,I4)')
     &   'F_NBL mc=3 NBL1=',NBL(1),' NBL2=',NBL(2),
     &   ' IBLTE1=',IBLTE(1),' IBLTE2=',IBLTE(2)
        DO 8890 ISD=1, 2
          DO 8891 IBL=2, NBL(ISD)
          WRITE(0,'(A,I1,A,I3,A,Z8,A,Z8,A,Z8)')
     &      'F_PFCM_OUT mc=3 s=', ISD, ' ibl=', IBL,
     &      ' T=', TRANSFER(THET(IBL,ISD),1),
     &      ' D=', TRANSFER(DSTR(IBL,ISD),1),
     &      ' U=', TRANSFER(UEDG(IBL,ISD),1)
 8891     CONTINUE
 8890   CONTINUE
      ENDIF
C
      DO 898 IS=1, 2
        DO 899 IBL=2, NBL(IS)
          WRITE(0,'(A,I1,A,I4,A,Z8,A,Z8,A,Z8,A,Z8,A,Z8,A,Z8)')
     &      'F_PM s=', IS, ' i=', IBL,
     &      ' T=', TRANSFER(THET(IBL,IS),1),
     &      ' D=', TRANSFER(DSTR(IBL,IS),1),
     &      ' U=', TRANSFER(UEDG(IBL,IS),1),
     &      ' C=', TRANSFER(CTAU(IBL,IS),1),
     &      ' M=', TRANSFER(MASS(IBL,IS),1),
     &      ' X=', TRANSFER(XSSI(IBL,IS),1)
 899    CONTINUE
 898  CONTINUE
C
      DO 5 IS=1, 2
        DO 6 IBL=2, NBL(IS)
          USAV(IBL,IS) = UEDG(IBL,IS)
    6   CONTINUE
    5 CONTINUE
C
      CALL UESET
C
      DO 7 IS=1, 2
        DO 8 IBL=2, NBL(IS)
          TEMP = USAV(IBL,IS)
          USAV(IBL,IS) = UEDG(IBL,IS)
          UEDG(IBL,IS) = TEMP
    8   CONTINUE
    7 CONTINUE
C
      ILE1 = IPAN(2,1)
      ILE2 = IPAN(2,2)
      ITE1 = IPAN(IBLTE(1),1)
      ITE2 = IPAN(IBLTE(2),2)
C
      JVTE1 = ISYS(IBLTE(1),1)
      JVTE2 = ISYS(IBLTE(2),2)
C
      DULE1 = UEDG(2,1) - USAV(2,1)
      DULE2 = UEDG(2,2) - USAV(2,2)
      IF(SETBL_COUNT.EQ.1) THEN
        DO 7800 IBLD=2, 5
          WRITE(0,'(A,I2,4(A,Z8))') 'F_USAV ibl=',IBLD,
     &     ' UINV=',TRANSFER(UINV(IBLD,1),1),
     &     ' USAV=',TRANSFER(USAV(IBLD,1),1),
     &     ' UEDG=',TRANSFER(UEDG(IBLD,1),1),
     &     ' MASS=',TRANSFER(MASS(IBLD,1),1)
 7800   CONTINUE
      ENDIF
      DGAMDBG = UEDG(2,2) + UEDG(2,1)
      WRITE(0,777) TRANSFER(SST_GO,1),
     & TRANSFER(UEDG(2,1),1),TRANSFER(UEDG(2,2),1),
     & TRANSFER(DGAMDBG,1),TRANSFER(XSSI(2,2),1)
 777  FORMAT('F_SST ',5(1X,Z8.8))
C
C---- set LE and TE Ue sensitivities wrt all m values
      DO 10 JS=1, 2
        DO 110 JBL=2, NBL(JS)
          J  = IPAN(JBL,JS)
          JV = ISYS(JBL,JS)
          ULE1_M(JV) = -VTI(       2,1)*VTI(JBL,JS)*DIJ(ILE1,J)
          ULE2_M(JV) = -VTI(       2,2)*VTI(JBL,JS)*DIJ(ILE2,J)
          UTE1_M(JV) = -VTI(IBLTE(1),1)*VTI(JBL,JS)*DIJ(ITE1,J)
          UTE2_M(JV) = -VTI(IBLTE(2),2)*VTI(JBL,JS)*DIJ(ITE2,J)
  110   CONTINUE
   10 CONTINUE
C
      ULE1_A = UINV_A(2,1)
      ULE2_A = UINV_A(2,2)
C
C---- trace pre-Newton UEDG at key stations
      WRITE(*,'(A,Z8,1X,Z8,1X,Z8,1X,Z8)')
     & 'F_PRE_NWT',
     & TRANSFER(UEDG(2,1),1),TRANSFER(UEDG(41,1),1),
     & TRANSFER(UEDG(2,2),1),TRANSFER(UEDG(41,2),1)
C
C**** Go over each boundary layer/wake
      DO 2000 IS=1, 2
C
C---- there is no station "1" at similarity, so zero everything out
      DO 20 JS=1, 2
        DO 210 JBL=2, NBL(JS)
          JV = ISYS(JBL,JS)
          U1_M(JV) = 0.
          D1_M(JV) = 0.
  210   CONTINUE
   20 CONTINUE
      U1_A = 0.
      D1_A = 0.
C
      DUE1 = 0.
      DDS1 = 0.
C
C---- similarity station pressure gradient parameter  x/u du/dx
      IBL = 2
      BULE = 1.0
C
C---- set forced transition arc length position
      CALL XIFSET(IS)
C
      TRAN = .FALSE.
      TURB = .FALSE.
C
C**** Sweep downstream setting up BL equation linearizations
      DO 1000 IBL=2, NBL(IS)
C
      IV  = ISYS(IBL,IS)
C
      SIMI = IBL.EQ.2
      WAKE = IBL.GT.IBLTE(IS)
      TRAN = IBL.EQ.ITRAN(IS)
      TURB = IBL.GT.ITRAN(IS)
C
      I = IPAN(IBL,IS)
C
C---- set primary variables for current station
      XSI = XSSI(IBL,IS)
      IF(IBL.LT.ITRAN(IS)) AMI = CTAU(IBL,IS)
      IF(IBL.GE.ITRAN(IS)) CTI = CTAU(IBL,IS)
      UEI = UEDG(IBL,IS)
      THI = THET(IBL,IS)
      MDI = MASS(IBL,IS)
C
      DSI = MDI/UEI
C---- Dump TE junction pre-update DSI at EVERY Newton iteration
      IF(IS.EQ.2 .AND. IBL.EQ.IBLTE(IS)+1) THEN
       WRITE(0,776) ITBL,
     &  TRANSFER(THI,1),TRANSFER(DSI,1),
     &  TRANSFER(UEI,1),TRANSFER(DSWAKI,1)
 776   FORMAT('F_TE_ITER it=',I2,4(1X,Z8.8))
      ENDIF
      IF(IS.EQ.1 .AND. IBL.EQ.3) THEN
       WRITE(0,778) TRANSFER(MDI,1),
     &  TRANSFER(UEI,1),TRANSFER(THI,1),
     &  TRANSFER(D1,1),TRANSFER(T1,1),TRANSFER(U1,1)
 778   FORMAT('F_SETBL2 ',6(1X,Z8.8))
      ENDIF
C
      IF(WAKE) THEN
       IW = IBL - IBLTE(IS)
       DSWAKI = WGAP(IW)
      ELSE
       DSWAKI = 0.
      ENDIF
C
C---- set derivatives of DSI (= D2)
      D2_M2 =  1.0/UEI
      D2_U2 = -DSI/UEI
C
      DO 30 JS=1, 2
        DO 310 JBL=2, NBL(JS)
          J  = IPAN(JBL,JS)
          JV = ISYS(JBL,JS)
          U2_M(JV) = -VTI(IBL,IS)*VTI(JBL,JS)*DIJ(I,J)
          D2_M(JV) = D2_U2*U2_M(JV)
  310   CONTINUE
   30 CONTINUE
      D2_M(IV) = D2_M(IV) + D2_M2
C
      U2_A = UINV_A(IBL,IS)
      D2_A = D2_U2*U2_A
C
C---- "forced" changes due to mismatch between UEDG and USAV=UINV+dij*MASS
      DUE2 = UEDG(IBL,IS) - USAV(IBL,IS)
      IF(IS.EQ.1 .AND. IBL.EQ.2) THEN
       DDS2DBG = D2_U2*DUE2
       WRITE(0,779) TRANSFER(DUE2,1),
     &  TRANSFER(UEDG(IBL,IS),1),
     &  TRANSFER(USAV(IBL,IS),1),
     &  TRANSFER(DSI,1),
     &  TRANSFER(DDS2DBG,1),
     &  TRANSFER(D2_U2,1)
 779   FORMAT('F_DUE2_2 ',6(1X,Z8.8))
      ENDIF
      IF(IS.EQ.2 .AND. IBL.EQ.16) THEN
       WRITE(0,'(A,6(1X,Z8.8))')
     &  'F_STN16',
     &  TRANSFER(UEDG(IBL,IS),1),TRANSFER(THI,1),
     &  TRANSFER(DSI,1),TRANSFER(CTI,1),
     &  TRANSFER(USAV(IBL,IS),1),TRANSFER(DUE2,1)
      ENDIF
      DDS2 = D2_U2*DUE2
C
C---- GDB parity hex dump of station state (include CTI/AMI for turbulent/laminar shear-lag)
      WRITE(0,'(A,I2.2,A,I2,A,I3,6(A,Z8))')
     & 'F_STN_c',SETBL_COUNT,' IS=',IS,' IBL=',IBL,
     & ' UEI=',TRANSFER(UEI,1),' THI=',TRANSFER(THI,1),
     & ' MDI=',TRANSFER(MDI,1),' DSI=',TRANSFER(DSI,1),
     & ' CTI=',TRANSFER(CTI,1),' AMI=',TRANSFER(AMI,1)
C
      CALL BLPRV(XSI,AMI,CTI,THI,DSI,DSWAKI,UEI)
      CALL BLKIN
      IF (TRACE_OUTER.EQ.1 .AND. IS.EQ.1 .AND. IBL.LE.4) THEN
        WRITE(0,'(A,I1,A,I2,6(A,Z8))') 'F_COM2 s=',IS,' ibl=',IBL,
     &   ' HK2=',TRANSFER(HK2,1),' RT2=',TRANSFER(RT2,1),
     &   ' HS2=',TRANSFER(HS2,1),' US2=',TRANSFER(US2,1),
     &   ' CF2=',TRANSFER(CF2,1),' DI2=',TRANSFER(DI2,1)
      ENDIF
C
C---- check for transition and set TRAN, XT, etc. if found
      IF(TRAN) THEN
        IF(IS.EQ.2 .AND. TRACE_OUTER.GE.8) THEN
         WRITE(0,'(A,I2,A,I3,A,Z8,A,Z8,A,Z8,A,Z8,A,Z8,A,Z8,A,Z8,A,Z8)')
     &    'F_SETBL_TR oi=',TRACE_OUTER,' i=',IBL,
     &    ' A1=',TRANSFER(AMPL1,1),
     &    ' AMI=',TRANSFER(AMI,1),
     &    ' X1=',TRANSFER(X1,1),
     &    ' X2=',TRANSFER(X2,1),
     &    ' T1=',TRANSFER(T1,1),
     &    ' T2=',TRANSFER(T2,1),
     &    ' D1=',TRANSFER(D1,1),
     &    ' D2=',TRANSFER(D2,1)
        ENDIF
        IF(IS.EQ.2 .AND. IBL.EQ.17 .AND. SETBL_COUNT.EQ.3) THEN
         WRITE(0,'(A,8(A,Z8))') 'F_SETBL_TR3_PRE',
     &    ' A1=',TRANSFER(AMPL1,1),' AMI=',TRANSFER(AMI,1),
     &    ' X1=',TRANSFER(X1,1),' X2=',TRANSFER(X2,1),
     &    ' T1=',TRANSFER(T1,1),' T2=',TRANSFER(T2,1),
     &    ' D1=',TRANSFER(D1,1),' D2=',TRANSFER(D2,1)
        ENDIF
        CALL TRCHEK
        AMI = AMPL2
        IF(IS.EQ.2 .AND. IBL.EQ.17 .AND. SETBL_COUNT.EQ.3) THEN
         WRITE(0,'(A,5(A,Z8),A,L1)') 'F_SETBL_TR3_POST',
     &    ' XT=',TRANSFER(XT,1),' A1=',TRANSFER(AMPL1,1),
     &    ' AMI=',TRANSFER(AMI,1),' AMPL2=',TRANSFER(AMPL2,1),
     &    ' AMCRIT=',TRANSFER(AMCRIT,1),' TRAN=',TRAN
        ENDIF
        IF(IS.EQ.2 .AND. IBL.EQ.5) THEN
         WRITE(0,'(A,Z8,A,Z8,A,Z8,A,Z8,A,Z8)')
     &    'F_XT5 XT=',TRANSFER(XT,1),
     &    ' A1=',TRANSFER(AMPL1,1),
     &    ' A2=',TRANSFER(AMI,1),
     &    ' X1=',TRANSFER(X1,1),
     &    ' X2=',TRANSFER(X2,1)
        ENDIF
      ENDIF
C---- (VS1 trace moved to after BLSYS for all calls)
      IF(IBL.EQ.ITRAN(IS) .AND. .NOT.TRAN) THEN
       WRITE(*,*) 'SETBL: Xtr???  n1 n2: ', AMPL1, AMPL2
      ENDIF
C
C---- assemble 10x4 linearized system for dCtau, dTh, dDs, dUe, dXi
C     at the previous "1" station and the current "2" station
C
      IF(IBL.EQ.IBLTE(IS)+1) THEN
C
C----- define quantities at start of wake, adding TE base thickness to Dstar
       TTE = THET(IBLTE(1),1) + THET(IBLTE(2),2)
       DTE = DSTR(IBLTE(1),1) + DSTR(IBLTE(2),2) + ANTE
       CTE = ( CTAU(IBLTE(1),1)*THET(IBLTE(1),1)
     &       + CTAU(IBLTE(2),2)*THET(IBLTE(2),2) ) / TTE
       CALL TESYS(CTE,TTE,DTE)
C
       TTE_TTE1 = 1.0
       TTE_TTE2 = 1.0
       DTE_MTE1 =               1.0 / UEDG(IBLTE(1),1)
       DTE_UTE1 = -DSTR(IBLTE(1),1) / UEDG(IBLTE(1),1)
       DTE_MTE2 =               1.0 / UEDG(IBLTE(2),2)
       DTE_UTE2 = -DSTR(IBLTE(2),2) / UEDG(IBLTE(2),2)
       CTE_CTE1 = THET(IBLTE(1),1)/TTE
       CTE_CTE2 = THET(IBLTE(2),2)/TTE
       CTE_TTE1 = (CTAU(IBLTE(1),1) - CTE)/TTE
       CTE_TTE2 = (CTAU(IBLTE(2),2) - CTE)/TTE
C
C----- re-define D1 sensitivities wrt m since D1 depends on both TE Ds values
       DO 35 JS=1, 2
         DO 350 JBL=2, NBL(JS)
           J  = IPAN(JBL,JS)
           JV = ISYS(JBL,JS)
           D1_M(JV) = DTE_UTE1*UTE1_M(JV) + DTE_UTE2*UTE2_M(JV)
  350    CONTINUE
   35  CONTINUE
       D1_M(JVTE1) = D1_M(JVTE1) + DTE_MTE1
       D1_M(JVTE2) = D1_M(JVTE2) + DTE_MTE2
C
C----- "forced" changes from  UEDG --- USAV=UINV+dij*MASS  mismatch
       DUE1 = 0.
       DDS1 = DTE_UTE1*(UEDG(IBLTE(1),1) - USAV(IBLTE(1),1))
     &      + DTE_UTE2*(UEDG(IBLTE(2),2) - USAV(IBLTE(2),2))
       WRITE(*,'(A,Z8,1X,Z8,1X,Z8,1X,Z8,1X,Z8,1X,Z8)')
     &  'F_TE_DDS1',
     &  TRANSFER(DDS1,1),TRANSFER(DTE_UTE1,1),TRANSFER(DTE_UTE2,1),
     &  TRANSFER(UEDG(IBLTE(1),1)-USAV(IBLTE(1),1),1),
     &  TRANSFER(UEDG(IBLTE(2),2)-USAV(IBLTE(2),2),1),
     &  TRANSFER(VSREZ(3),1)
C
      ELSE
C
       TRACE_SIDE = IS
       TRACE_STATION = IBL
       TRACE_ITER = SETBL_COUNT
       CALL BLSYS(1)
C
C---- trace BLVAR output at first Newton iter, station 2 side 1
       WRITE(*,'(A,I1,I3,1X,Z8,1X,Z8,1X,Z8,1X,Z8)')
     &  'F_N',IS,IBL,
     &  TRANSFER(HK2,1),TRANSFER(VSREZ(2),1),
     &  TRANSFER(VSREZ(3),1),TRANSFER(THI,1)
      ENDIF
C---- Per-station VS2/VS1/RES/VSX XOR-hash at SETBL call 1-18 (sign-sensitive)
      IF(SETBL_COUNT.GE.1 .AND. SETBL_COUNT.LE.18) THEN
       IVSH = 0
       DO 9951 JJR=1, 3
         DO 9952 JJC=1, 5
           IVSH = IEOR(IVSH, TRANSFER(VS2(JJR,JJC),1))
 9952    CONTINUE
 9951  CONTINUE
       IV1H = 0
       DO 9954 JJR=1, 3
         DO 9955 JJC=1, 5
           IV1H = IEOR(IV1H, TRANSFER(VS1(JJR,JJC),1))
 9955    CONTINUE
 9954  CONTINUE
       IRSH = 0
       DO 9953 JJR=1, 3
         IRSH = IEOR(IRSH, TRANSFER(VSREZ(JJR),1))
 9953  CONTINUE
       IVXH = 0
       DO 9956 JJR=1, 3
         IVXH = IEOR(IVXH, TRANSFER(VSX(JJR),1))
 9956  CONTINUE
       WRITE(0,'(A,I2.2,A,I1,A,I4,A,Z8,A,Z8,A,Z8,A,Z8)')
     &  'F_STN',SETBL_COUNT,' s=',IS,' i=',IBL,
     &  ' VS2X=',IVSH,' VS1X=',IV1H,' RESX=',IRSH,' VSXX=',IVXH
      ENDIF
C---- NACA 0012 2M a=3: dump TRAN flag at call 2 side 2 station 65
      IF(SETBL_COUNT.EQ.2 .AND. IS.EQ.2 .AND. IBL.EQ.65) THEN
       WRITE(0,'(A,L2,A,L2,A,I4,A,I4)')
     &  'F_TRAN_STN65 tran=',TRAN,' turb=',TURB,
     &  ' ITRAN2=',ITRAN(2),' IBLTE2=',IBLTE(2)
      ENDIF
C---- NACA 0012 2M a=3: per-cell at call 2 side 2 station 66 (post-trans)
      IF(SETBL_COUNT.EQ.2 .AND. IS.EQ.2 .AND. IBL.EQ.66) THEN
       DO 9982 JJR=1, 3
        WRITE(0,'(A,I1,5(A,Z8),A,Z8)')
     &   'F_VS2_0212066 r',JJR-1,
     &   ' c0=',TRANSFER(VS2(JJR,1),1),
     &   ' c1=',TRANSFER(VS2(JJR,2),1),
     &   ' c2=',TRANSFER(VS2(JJR,3),1),
     &   ' c3=',TRANSFER(VS2(JJR,4),1),
     &   ' c4=',TRANSFER(VS2(JJR,5),1),
     &   ' R=',TRANSFER(VSREZ(JJR),1)
 9982  CONTINUE
      ENDIF
C---- NACA 0012 2M a=3: per-cell at call 2 side 2 station 65 (ITRAN)
      IF(SETBL_COUNT.EQ.2 .AND. IS.EQ.2 .AND. IBL.EQ.65) THEN
       DO 9981 JJR=1, 3
        WRITE(0,'(A,I1,5(A,Z8),A,Z8)')
     &   'F_VS2_0212065 r',JJR-1,
     &   ' c0=',TRANSFER(VS2(JJR,1),1),
     &   ' c1=',TRANSFER(VS2(JJR,2),1),
     &   ' c2=',TRANSFER(VS2(JJR,3),1),
     &   ' c3=',TRANSFER(VS2(JJR,4),1),
     &   ' c4=',TRANSFER(VS2(JJR,5),1),
     &   ' R=',TRANSFER(VSREZ(JJR),1)
 9981  CONTINUE
      ENDIF
C---- s1210 IBL=2 call=1 VS1/VS2 dump
      IF(SETBL_COUNT.EQ.1 .AND. IS.EQ.1 .AND. IBL.EQ.2) THEN
       DO 9971 JJR=1, 3
        WRITE(0,'(A,I1,5(A,Z8))')
     &   'F_S1210_VS2 r',JJR-1,
     &   ' c0=',TRANSFER(VS2(JJR,1),1),
     &   ' c1=',TRANSFER(VS2(JJR,2),1),
     &   ' c2=',TRANSFER(VS2(JJR,3),1),
     &   ' c3=',TRANSFER(VS2(JJR,4),1),
     &   ' c4=',TRANSFER(VS2(JJR,5),1)
        WRITE(0,'(A,I1,5(A,Z8))')
     &   'F_S1210_VS1 r',JJR-1,
     &   ' c0=',TRANSFER(VS1(JJR,1),1),
     &   ' c1=',TRANSFER(VS1(JJR,2),1),
     &   ' c2=',TRANSFER(VS1(JJR,3),1),
     &   ' c3=',TRANSFER(VS1(JJR,4),1),
     &   ' c4=',TRANSFER(VS1(JJR,5),1)
 9971  CONTINUE
       WRITE(0,'(A,3(A,Z8))')
     &   'F_S1210_VSX',
     &   ' x0=',TRANSFER(VSX(1),1),
     &   ' x1=',TRANSFER(VSX(2),1),
     &   ' x2=',TRANSFER(VSX(3),1)
       WRITE(0,'(A,3(A,Z8))')
     &   'F_S1210_VSREZ',
     &   ' r0=',TRANSFER(VSREZ(1),1),
     &   ' r1=',TRANSFER(VSREZ(2),1),
     &   ' r2=',TRANSFER(VSREZ(3),1)
       WRITE(0,'(A,6(A,Z8))') 'F_S1210_COM2',
     &   ' HK2=',TRANSFER(HK2,1),
     &   ' RT2=',TRANSFER(RT2,1),
     &   ' HS2=',TRANSFER(HS2,1),
     &   ' US2=',TRANSFER(US2,1),
     &   ' CF2=',TRANSFER(CF2,1),
     &   ' DI2=',TRANSFER(DI2,1)
      ENDIF
C---- Full VS2/VS1 per-cell trace at SETBL call 3, side 2 station 73 (transition)
      IF(SETBL_COUNT.EQ.3 .AND. IS.EQ.2 .AND. IBL.EQ.73) THEN
       DO 9961 JJR=1, 3
        WRITE(0,'(A,I2.2,A,I1,A,I3,A,I1,5(A,Z8))')
     &   'F_VS2_',SETBL_COUNT,' s',IS,'i',IBL,'r',JJR-1,
     &   ' c0=',TRANSFER(VS2(JJR,1),1),
     &   ' c1=',TRANSFER(VS2(JJR,2),1),
     &   ' c2=',TRANSFER(VS2(JJR,3),1),
     &   ' c3=',TRANSFER(VS2(JJR,4),1),
     &   ' c4=',TRANSFER(VS2(JJR,5),1)
        WRITE(0,'(A,I2.2,A,I1,A,I3,A,I1,5(A,Z8))')
     &   'F_VS1_',SETBL_COUNT,' s',IS,'i',IBL,'r',JJR-1,
     &   ' c0=',TRANSFER(VS1(JJR,1),1),
     &   ' c1=',TRANSFER(VS1(JJR,2),1),
     &   ' c2=',TRANSFER(VS1(JJR,3),1),
     &   ' c3=',TRANSFER(VS1(JJR,4),1),
     &   ' c4=',TRANSFER(VS1(JJR,5),1)
 9961  CONTINUE
      ENDIF
C---- Per-station residual at outer iteration 8
      IF(TRACE_OUTER.EQ.8) THEN
       WRITE(0,'(A,I1,A,I4,A,Z8,A,Z8,A,Z8)')
     &  'F_VDEL8 s=',IS,' i=',IBL,
     &  ' R0=',TRANSFER(VSREZ(1),1),
     &  ' R1=',TRANSFER(VSREZ(2),1),
     &  ' R2=',TRANSFER(VSREZ(3),1)
      ENDIF
C---- removed - will trace after update instead
C---- Trace VS1 at station 5 side 2 AFTER BLSYS
      IF(TRAN .AND. IS.EQ.2 .AND. IBL.EQ.5) THEN
       DO 9801 JJR=1, 3
        WRITE(0,'(A,I1,A,Z8,1X,Z8,1X,Z8,1X,Z8)')
     &   'F_VS1_5 r',JJR-1,':',
     &   TRANSFER(VS1(JJR,1),1),TRANSFER(VS1(JJR,2),1),
     &   TRANSFER(VS1(JJR,3),1),TRANSFER(VS1(JJR,4),1)
 9801  CONTINUE
      ENDIF
C---- IMMEDIATE post-BLSYS VSX check
      IF(IS.EQ.1 .AND. IBL.EQ.79) THEN
       WRITE(*,'(A,Z8,A,L1,A,L1,A,L1)')
     &  'F_POST_BLSYS VSX1=',TRANSFER(VSX(1),1),
     &  ' TRAN=',TRAN,' TURB=',TURB,' WAKE=',WAKE
      ENDIF
C
C---- GDB parity hex dump of BLSYS output
      WRITE(0,'(A,I2,A,I3,A,Z8,A,Z8,A,Z8)')
     & 'F_VSREZ IS=',IS,' IBL=',IBL,
     & ' R1=',TRANSFER(VSREZ(1),1),
     & ' R2=',TRANSFER(VSREZ(2),1),
     & ' R3=',TRANSFER(VSREZ(3),1)
C
C---- Trace HKLIM at station 23 side 2
      IF(IS.EQ.2 .AND. IBL.EQ.23) THEN
       WRITE(0,'(A,4(1X,Z8.8))')
     &  'F_HKLIM23',
     &  TRANSFER(1.02000*THI,1),
     &  TRANSFER(DSI,1),
     &  TRANSFER(1.02000,1),
     &  TRANSFER(THI,1)
      ENDIF
C---- Trace SETBL fresh COM1 at station 23 side 2
      IF(IS.EQ.2 .AND. IBL.EQ.23) THEN
       WRITE(0,'(A,6(1X,Z8.8))')
     &  'F_SETBL23_COM1',
     &  TRANSFER(HK1,1),TRANSFER(HS1,1),
     &  TRANSFER(CF1,1),TRANSFER(THI,1),
     &  TRANSFER(DSI,1),TRANSFER(UEI,1)
      ENDIF
C---- Trace SETBL fresh COM2 at station 16 side 2 (this becomes COM1 for stn 17)
      IF(IS.EQ.2 .AND. IBL.EQ.16) THEN
       WRITE(0,'(A,5(1X,Z8.8))')
     &  'F_SETBL16',
     &  TRANSFER(HK2,1),TRANSFER(HS2,1),
     &  TRANSFER(CF2,1),TRANSFER(CQ2,1),
     &  TRANSFER(DI2,1)
      ENDIF
C---- And trace COM1 (= upstream fresh BLVAR output from station 15)
      IF(IS.EQ.2 .AND. IBL.EQ.16) THEN
       WRITE(0,'(A,5(1X,Z8.8))')
     &  'F_SETBL16_COM1',
     &  TRANSFER(HK1,1),TRANSFER(HS1,1),
     &  TRANSFER(CF1,1),TRANSFER(CQ1,1),
     &  TRANSFER(DI1,1)
      ENDIF
C---- Save wall shear and equil. max shear coefficient for plotting output
      TAU(IBL,IS) = 0.5*R2*U2*U2*CF2
      DIS(IBL,IS) =     R2*U2*U2*U2*DI2*HS2*0.5
      CTQ(IBL,IS) = CQ2
      DELT(IBL,IS) = DE2
      USLP(IBL,IS) = 1.60/(1.0+US2)
C
C@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@
c      IF(WAKE) THEN
c        ALD = DLCON
c      ELSE
c       ALD = 1.0
c      ENDIF
cC
c      IF(TURB .AND. .NOT.WAKE) THEN
c        GCC = GCCON
c        HKC     = HK2 - 1.0 - GCC/RT2
c        IF(HKC .LT. 0.01) THEN
c         HKC = 0.01
c        ENDIF
c       ELSE
c        HKC = HK2 - 1.0
c       ENDIF
cC
c       HR = HKC     / (GACON*ALD*HK2)
c       UQ = (0.5*CF2 - HR**2) / (GBCON*D2)
cC
c       IF(TURB) THEN
c        IBLP = MIN(IBL+1,NBL(IS))
c        IBLM = MAX(IBL-1,2      )
c        DXSSI = XSSI(IBLP,IS) - XSSI(IBLM,IS)
c        IF(DXXSI.EQ.0.0) DXSSI = 1.0
c        GUXD(IBL,IS) = -LOG(UEDG(IBLP,IS)/UEDG(IBLM,IS)) / DXSSI
c        GUXQ(IBL,IS) = -UQ
c       ELSE
c        GUXD(IBL,IS) = 0.0
c        GUXQ(IBL,IS) = 0.0
c       ENDIF
C@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@
C
C---- set XI sensitivities wrt LE Ue changes
      IF(IS.EQ.1) THEN
       XI_ULE1 =  SST_GO
       XI_ULE2 = -SST_GP
      ELSE
       XI_ULE1 = -SST_GO
       XI_ULE2 =  SST_GP
      ENDIF
C
C---- stuff BL system coefficients into main Jacobian matrix
C
      DO 40 JV=1, NSYS
        VM(1,JV,IV) = VS1(1,3)*D1_M(JV) + VS1(1,4)*U1_M(JV)
     &              + VS2(1,3)*D2_M(JV) + VS2(1,4)*U2_M(JV)
     &              + (VS1(1,5) + VS2(1,5) + VSX(1))
     &               *(XI_ULE1*ULE1_M(JV) + XI_ULE2*ULE2_M(JV))
        IF(IV.EQ.160 .AND. SETBL_COUNT.EQ.14 .AND.
     &     (JV.EQ.158.OR.JV.EQ.159.OR.JV.EQ.160)) THEN
         WRITE(0,'(A,I4,A,Z8,A,Z8,A,Z8,A,Z8,A,Z8,A,Z8)')
     &    'F_VMA jv=',JV,
     &    ' vm=',TRANSFER(VM(1,JV,IV),1),
     &    ' d2m=',TRANSFER(D2_M(JV),1),
     &    ' u2m=',TRANSFER(U2_M(JV),1),
     &    ' xi=',TRANSFER((VS1(1,5)+VS2(1,5)+VSX(1))
     &                    *(XI_ULE1*ULE1_M(JV)+XI_ULE2*ULE2_M(JV)),1),
     &    ' xic=',TRANSFER(VS1(1,5)+VS2(1,5)+VSX(1),1),
     &    ' s2d=',TRANSFER(VS2(1,3),1)
        ENDIF
C--- Trace VM[k=0,JV,IV=1] at similarity iter 1 parity debug (k=0 always zero at sim)
        IF(IV.EQ.1 .AND. JV.LE.10 .AND. SETBL_COUNT.EQ.1) THEN
         WRITE(0,9901) JV,TRANSFER(VM(1,JV,IV),1),
     &    TRANSFER(D1_M(JV),1),TRANSFER(U1_M(JV),1),
     &    TRANSFER(D2_M(JV),1),TRANSFER(U2_M(JV),1),
     &    TRANSFER(ULE1_M(JV),1),TRANSFER(ULE2_M(JV),1)
 9901    FORMAT('F_VM_K0_JV',I2,' vm=',Z8,' d1m=',Z8,' u1m=',Z8,
     &    ' d2m=',Z8,' u2m=',Z8,' ule1=',Z8,' ule2=',Z8)
        ENDIF
   40 CONTINUE
C (VM dump moved to after all 3 rows)
C
      VB(1,1,IV) = VS1(1,1)
      VB(1,2,IV) = VS1(1,2)
C
      VA(1,1,IV) = VS2(1,1)
      VA(1,2,IV) = VS2(1,2)
C
      IF(LALFA) THEN
       VDEL(1,2,IV) = VSR(1)*RE_CLMR + VSM(1)*MSQ_CLMR
      ELSE
       VDEL(1,2,IV) = 
     &       (VS1(1,4)*U1_A + VS1(1,3)*D1_A)
     &     + (VS2(1,4)*U2_A + VS2(1,3)*D2_A)
     &     + (VS1(1,5) + VS2(1,5) + VSX(1))
     &      *(XI_ULE1*ULE1_A + XI_ULE2*ULE2_A)
      ENDIF
C
      VDEL(1,1,IV) = VSREZ(1)
     &   + (VS1(1,4)*DUE1 + VS1(1,3)*DDS1)
     &   + (VS2(1,4)*DUE2 + VS2(1,3)*DDS2)
     &   + (VS1(1,5) + VS2(1,5) + VSX(1))
     &    *(XI_ULE1*DULE1 + XI_ULE2*DULE2)
      IF(IV.EQ.160 .AND. SETBL_COUNT.EQ.33) THEN
       WRITE(0,'(A,I3,A,I3,A,I1,A,Z8.8)')
     & 'F_VDEL160_WRITE at IBL=',IBL,' IV=',IV,' IS=',IS,
     & ' VDEL=',TRANSFER(VDEL(1,1,IV),1)
      ENDIF
      IF(IS.EQ.2 .AND. IBL.EQ.69 .AND. SETBL_COUNT.EQ.33) THEN
       WRITE(0,'(A,12(1X,A,Z8.8))')
     & 'F_VD67 k=1',
     & 'RES=',TRANSFER(VSREZ(1),1),
     & 'DUE1=',TRANSFER(DUE1,1),'DDS1=',TRANSFER(DDS1,1),
     & 'DUE2=',TRANSFER(DUE2,1),'DDS2=',TRANSFER(DDS2,1),
     & 'DULE1=',TRANSFER(DULE1,1),'DULE2=',TRANSFER(DULE2,1),
     & 'S13=',TRANSFER(VS1(1,3),1),'S14=',TRANSFER(VS1(1,4),1),
     & 'S15=',TRANSFER(VS1(1,5),1),'S23=',TRANSFER(VS2(1,3),1),
     & 'VDEL=',TRANSFER(VDEL(1,1,IV),1)
       WRITE(0,'(A,8(1X,A,Z8.8))')
     & 'F_VD67b',
     & 'S24=',TRANSFER(VS2(1,4),1),'S25=',TRANSFER(VS2(1,5),1),
     & 'VSX1=',TRANSFER(VSX(1),1),
     & 'XIU1=',TRANSFER(XI_ULE1,1),'XIU2=',TRANSFER(XI_ULE2,1)
      ENDIF
      IF(IS.EQ.1 .AND. IBL.EQ.2 .AND. SETBL_COUNT.EQ.1) THEN
       WRITE(0,'(A,A,Z8,A,Z8,A,Z8,A,Z8,A,Z8,A,Z8,A,Z8,A,Z8,A,Z8,A,Z8)')
     &  'F_VDASM_IBL2 k=1',
     &  ' RES=',TRANSFER(VSREZ(1),1),
     &  ' V1D=',TRANSFER(VS1(1,4)*DUE1,1),
     &  ' V1S=',TRANSFER(VS1(1,3)*DDS1,1),
     &  ' V2D=',TRANSFER(VS2(1,4)*DUE2,1),
     &  ' V2S=',TRANSFER(VS2(1,3)*DDS2,1),
     &  ' XI=',TRANSFER((VS1(1,5)+VS2(1,5)+VSX(1))
     &                   *(XI_ULE1*DULE1+XI_ULE2*DULE2),1),
     &  ' DU1=',TRANSFER(DUE1,1),
     &  ' DU2=',TRANSFER(DUE2,1),
     &  ' DD1=',TRANSFER(DDS1,1),
     &  ' DD2=',TRANSFER(DDS2,1)
      ENDIF
      IF(IS.EQ.2 .AND. IBL.EQ.5) THEN
       WRITE(0,'(A,Z8,A,Z8,A,Z8,A,Z8,A,Z8,A,Z8,
     &  A,Z8,A,Z8,A,Z8,A,Z8,A,Z8,A,Z8,A,Z8,
     &  A,Z8,A,Z8,A,Z8,A,Z8,A,Z8)')
     &  'F_VDASM'//
     &  ' RES=',TRANSFER(VSREZ(1),1),
     &  ' V1D=',TRANSFER(VS1(1,4)*DUE1,1),
     &  ' V1S=',TRANSFER(VS1(1,3)*DDS1,1),
     &  ' V2D=',TRANSFER(VS2(1,4)*DUE2,1),
     &  ' V2S=',TRANSFER(VS2(1,3)*DDS2,1),
     &  ' XI=',TRANSFER((VS1(1,5)+VS2(1,5)+VSX(1))
     &                   *(XI_ULE1*DULE1+XI_ULE2*DULE2),1),
     &  ' P1=',TRANSFER(VS1(1,4)*DUE1+VS1(1,3)*DDS1,1),
     &  ' P2=',TRANSFER(VS2(1,4)*DUE2+VS2(1,3)*DDS2,1),
     &  ' ACC=',TRANSFER(VDEL(1,1,IV),1),
     &  ' S13=',TRANSFER(VS1(1,3),1),
     &  ' S14=',TRANSFER(VS1(1,4),1),
     &  ' S23=',TRANSFER(VS2(1,3),1),
     &  ' S24=',TRANSFER(VS2(1,4),1),
     &  ' DD1=',TRANSFER(DDS1,1),
     &  ' DD2=',TRANSFER(DDS2,1),
     &  ' DU1=',TRANSFER(DUE1,1),
     &  ' DU2=',TRANSFER(DUE2,1)
      ENDIF
C
C
      DO 50 JV=1, NSYS
        VM(2,JV,IV) = VS1(2,3)*D1_M(JV) + VS1(2,4)*U1_M(JV)
     &              + VS2(2,3)*D2_M(JV) + VS2(2,4)*U2_M(JV)
     &              + (VS1(2,5) + VS2(2,5) + VSX(2))
     &               *(XI_ULE1*ULE1_M(JV) + XI_ULE2*ULE2_M(JV))
C--- Trace VM[k=1,JV,IV=1] at similarity iter 1 parity debug
        IF(IV.EQ.1 .AND. JV.LE.10 .AND. SETBL_COUNT.EQ.1) THEN
         WRITE(0,9911) JV,TRANSFER(VM(2,JV,IV),1),
     &    TRANSFER(VS2(2,3),1),TRANSFER(VS2(2,4),1),
     &    TRANSFER(D2_M(JV),1),TRANSFER(U2_M(JV),1)
 9911    FORMAT('F_VM1_JV',I2,' vm=',Z8,' vs2d=',Z8,' vs2u=',Z8,
     &    ' d2m=',Z8,' u2m=',Z8)
        ENDIF
   50 CONTINUE
C
      VB(2,1,IV)  = VS1(2,1)
      VB(2,2,IV)  = VS1(2,2)
C
      VA(2,1,IV) = VS2(2,1)
      VA(2,2,IV) = VS2(2,2)
C
      IF(LALFA) THEN
       VDEL(2,2,IV) = VSR(2)*RE_CLMR + VSM(2)*MSQ_CLMR
      ELSE
       VDEL(2,2,IV) = 
     &       (VS1(2,4)*U1_A + VS1(2,3)*D1_A)
     &     + (VS2(2,4)*U2_A + VS2(2,3)*D2_A)
     &     + (VS1(2,5) + VS2(2,5) + VSX(2))
     &      *(XI_ULE1*ULE1_A + XI_ULE2*ULE2_A)
      ENDIF
C
      VDEL(2,1,IV) = VSREZ(2)
     &   + (VS1(2,4)*DUE1 + VS1(2,3)*DDS1)
     &   + (VS2(2,4)*DUE2 + VS2(2,3)*DDS2)
     &   + (VS1(2,5) + VS2(2,5) + VSX(2))
     &    *(XI_ULE1*DULE1 + XI_ULE2*DULE2)
      IF(IS.EQ.1 .AND. IBL.EQ.2) THEN
       WRITE(0,'(A,Z8,A,Z8,A,Z8,A,Z8,A,Z8,A,Z8,
     &  A,Z8,A,Z8,A,Z8,A,Z8,A,Z8,A,Z8,A,Z8,
     &  A,Z8,A,Z8,A,Z8,A,Z8,A,Z8,A,Z8)')
     &  'F_VDASM2'//
     &  ' RES=',TRANSFER(VSREZ(2),1),
     &  ' V1D=',TRANSFER(VS1(2,4)*DUE1,1),
     &  ' V1S=',TRANSFER(VS1(2,3)*DDS1,1),
     &  ' V2D=',TRANSFER(VS2(2,4)*DUE2,1),
     &  ' V2S=',TRANSFER(VS2(2,3)*DDS2,1),
     &  ' XI=',TRANSFER((VS1(2,5)+VS2(2,5)+VSX(2))
     &                   *(XI_ULE1*DULE1+XI_ULE2*DULE2),1),
     &  ' P1=',TRANSFER(VS1(2,4)*DUE1+VS1(2,3)*DDS1,1),
     &  ' P2=',TRANSFER(VS2(2,4)*DUE2+VS2(2,3)*DDS2,1),
     &  ' ACC=',TRANSFER(VDEL(2,1,IV),1),
     &  ' S13=',TRANSFER(VS1(2,3),1),
     &  ' S14=',TRANSFER(VS1(2,4),1),
     &  ' S15=',TRANSFER(VS1(2,5),1),
     &  ' S23=',TRANSFER(VS2(2,3),1),
     &  ' S24=',TRANSFER(VS2(2,4),1),
     &  ' S25=',TRANSFER(VS2(2,5),1),
     &  ' VSX=',TRANSFER(VSX(2),1),
     &  ' DD1=',TRANSFER(DDS1,1),
     &  ' DD2=',TRANSFER(DDS2,1),
     &  ' DU1=',TRANSFER(DUE1,1),
     &  ' DU2=',TRANSFER(DUE2,1),
     &  ' XIF=',TRANSFER(XI_ULE1*DULE1 + XI_ULE2*DULE2,1)
      ENDIF
C
C
      DO 60 JV=1, NSYS
        VM(3,JV,IV) = VS1(3,3)*D1_M(JV) + VS1(3,4)*U1_M(JV)
     &              + VS2(3,3)*D2_M(JV) + VS2(3,4)*U2_M(JV)
     &              + (VS1(3,5) + VS2(3,5) + VSX(3))
     &               *(XI_ULE1*ULE1_M(JV) + XI_ULE2*ULE2_M(JV))
        IF(IV.EQ.77 .AND. JV.EQ.1 .AND. TRACE_OUTER.EQ.5) THEN
          WRITE(0,'(A,I3,A,I3,A,I3)')
     &     'F_IVINFO77 IS=',IS,' IBL=',IBL,' IV=',IV
          WRITE(0,'(A,I3,A,I3,15(1X,A,Z8.8))')
     &     'F_VMIN77 iv=',IV,' jv=',JV,
     &     'vs1d=',TRANSFER(REAL(VS1(3,3)),1),
     &     'vs1u=',TRANSFER(REAL(VS1(3,4)),1),
     &     'vs2d=',TRANSFER(REAL(VS2(3,3)),1),
     &     'vs2u=',TRANSFER(REAL(VS2(3,4)),1),
     &     'vs15=',TRANSFER(REAL(VS1(3,5)),1),
     &     'vs25=',TRANSFER(REAL(VS2(3,5)),1),
     &     'vsx=',TRANSFER(REAL(VSX(3)),1),
     &     'd1mj=',TRANSFER(REAL(D1_M(JV)),1),
     &     'u1mj=',TRANSFER(REAL(U1_M(JV)),1),
     &     'd2mj=',TRANSFER(REAL(D2_M(JV)),1),
     &     'u2mj=',TRANSFER(REAL(U2_M(JV)),1),
     &     'ule1=',TRANSFER(REAL(ULE1_M(JV)),1),
     &     'ule2=',TRANSFER(REAL(ULE2_M(JV)),1),
     &     'xu1=',TRANSFER(REAL(XI_ULE1),1),
     &     'xu2=',TRANSFER(REAL(XI_ULE2),1)
          WRITE(0,'(A,Z8.8)')
     &     'F_VMOUT77 vm=',TRANSFER(REAL(VM(3,JV,IV)),1)
        ENDIF
   60 CONTINUE
C
      VB(3,1,IV) = VS1(3,1)
      VB(3,2,IV) = VS1(3,2)
C
      VA(3,1,IV) = VS2(3,1)
      VA(3,2,IV) = VS2(3,2)
C
      IF(LALFA) THEN
       VDEL(3,2,IV) = VSR(3)*RE_CLMR + VSM(3)*MSQ_CLMR
      ELSE
       VDEL(3,2,IV) = 
     &       (VS1(3,4)*U1_A + VS1(3,3)*D1_A)
     &     + (VS2(3,4)*U2_A + VS2(3,3)*D2_A)
     &     + (VS1(3,5) + VS2(3,5) + VSX(3))
     &      *(XI_ULE1*ULE1_A + XI_ULE2*ULE2_A)
      ENDIF
C
      VDEL(3,1,IV) = VSREZ(3)
     &   + (VS1(3,4)*DUE1 + VS1(3,3)*DDS1)
     &   + (VS2(3,4)*DUE2 + VS2(3,3)*DDS2)
     &   + (VS1(3,5) + VS2(3,5) + VSX(3))
     &    *(XI_ULE1*DULE1 + XI_ULE2*DULE2)
C
C---- trace VM band at station 160 during SETBL call 14
      IF(IV.EQ.160 .AND. SETBL_COUNT.EQ.14) THEN
       DO 7951 IVMB=MAX(1,IV-3), MIN(NSYS,IV+3)
         WRITE(0,'(A,I4,3(1X,Z8))')
     &    'F_VM160 jv=',IVMB,
     &    TRANSFER(VM(1,IVMB,IV),1),
     &    TRANSFER(VM(2,IVMB,IV),1),
     &    TRANSFER(VM(3,IVMB,IV),1)
 7951  CONTINUE
      ENDIF
C---- trace VS2 at station 160 during SETBL call 14
      IF(IV.EQ.160 .AND. SETBL_COUNT.EQ.14) THEN
       WRITE(0,'(A,5(1X,Z8))')
     &  'F_VS1_160',
     &  TRANSFER(VS1(1,3),1),TRANSFER(VS1(1,4),1),
     &  TRANSFER(VS1(1,5),1),TRANSFER(VS2(1,5),1),
     &  TRANSFER(VSX(1),1)
       WRITE(0,'(A,12(1X,Z8))')
     &  'F_VS2_160',
     &  TRANSFER(VS2(1,1),1),TRANSFER(VS2(1,2),1),
     &  TRANSFER(VS2(1,3),1),TRANSFER(VS2(1,4),1),
     &  TRANSFER(VS2(2,1),1),TRANSFER(VS2(2,2),1),
     &  TRANSFER(VS2(2,3),1),TRANSFER(VS2(2,4),1),
     &  TRANSFER(VS2(3,1),1),TRANSFER(VS2(3,2),1),
     &  TRANSFER(VS2(3,3),1),TRANSFER(VS2(3,4),1)
      ENDIF
C---- per-IV VM row hash at SETBL call 2 (additive, matching C#)
      IF(SETBL_COUNT.EQ.2) THEN
       IVMH2 = 0
       IVDH2 = 0
       DO 7941 IKR=1, 3
         DO 7942 IKC=1, NSYS
           IVMH2 = IVMH2 +
     &       IAND(TRANSFER(VM(IKR,IKC,IV),1), 2147483647)
 7942    CONTINUE
         IVDH2 = IVDH2 +
     &     IAND(TRANSFER(VDEL(IKR,1,IV),1), 2147483647)
 7941  CONTINUE
       WRITE(0,'(A,I4,A,Z8,A,Z8)')
     &  'F_VMROW2 iv=',IV,' VM=',IVMH2,' VD=',IVDH2
      ENDIF
C---- dump off-diagonal VM at IV=1 (IS=1 IBL=2)
C------ VB element trace at divergent stations
      IF(IV.EQ.148.OR.IV.EQ.150.OR.IV.EQ.170) THEN
       WRITE(0,'(A,I4,1X,Z8,1X,Z8,1X,Z8,1X,Z8,1X,Z8,1X,Z8)')
     &  'F_VB_EL iv=',IV,
     &  TRANSFER(VB(1,1,IV),1),TRANSFER(VB(1,2,IV),1),
     &  TRANSFER(VB(2,1,IV),1),TRANSFER(VB(2,2,IV),1),
     &  TRANSFER(VB(3,1,IV),1),TRANSFER(VB(3,2,IV),1)
      ENDIF
C------ VS1 row 1 at wake station 7 (IBL=90)
      IF(IS.EQ.2 .AND. IBL.EQ.90) THEN
       WRITE(0,'(A,Z8,1X,Z8,1X,Z8,1X,Z8)')
     &  'F_VS90 ',
     &  TRANSFER(VS1(1,3),1),TRANSFER(VS1(1,4),1),
     &  TRANSFER(VS2(1,3),1),TRANSFER(VS2(1,4),1)
      ENDIF
C------ per-row VM hash at IV=167 (wake station 7)
      IF(IS.EQ.2 .AND. IBL.EQ.90) THEN
       DO 7930 KK=1, 3
         IHH2 = 0
         DO 7931 JVV=1, NSYS
           IHH2 = IEOR(IHH2, TRANSFER(VM(KK,JVV,IV),1))
 7931    CONTINUE
         WRITE(0,'(A,I1,A,Z8)') 'F_VM167_ROW k=',KK,' h=',IHH2
 7930  CONTINUE
      ENDIF
C------ VS and transition-point sens trace at IS=2 IBL=50
      IF(IS.EQ.2 .AND. IBL.EQ.50) THEN
       WRITE(0,'(A,Z8,1X,Z8,1X,Z8,1X,Z8)')
     &  'F_TP50 ',
     &  TRANSFER(XT_D1,1),
     &  TRANSFER(XT_T1,1),
     &  TRANSFER(XT_U1,1),
     &  TRANSFER(XT_A1,1)
       WRITE(0,'(A,Z8,1X,Z8,1X,Z8,1X,Z8,1X,Z8,1X,Z8)')
     &  'F_VS50 ',
     &  TRANSFER(VS1(2,3),1),TRANSFER(VS1(2,4),1),
     &  TRANSFER(VS2(2,3),1),TRANSFER(VS2(2,4),1),
     &  TRANSFER(VS1(3,3),1),TRANSFER(VS1(3,4),1)
      ENDIF
C------ per-row VM hash at transition station IV=127
      IF(IS.EQ.2 .AND. IBL.EQ.50) THEN
       DO 7910 KK=1, 3
         IHH2 = 0
         DO 7911 JVV=1, NSYS
           IHH2 = IEOR(IHH2, TRANSFER(VM(KK,JVV,IV),1))
 7911    CONTINUE
         WRITE(0,'(A,I1,A,Z8)') 'F_VM127_ROW k=',KK,' hash=',IHH2
 7910  CONTINUE
      ENDIF
      IF(IS.EQ.1 .AND. IBL.EQ.2) THEN
       WRITE(0,'(A,I3,A,Z8,A,Z8,A,Z8)')
     &  'F_VM_FIN IV=',IV,' VM21=',TRANSFER(VM(2,1,IV),1),
     &  ' VM22=',TRANSFER(VM(2,2,IV),1),
     &  ' VM31=',TRANSFER(VM(3,1,IV),1)
      ENDIF
C---- GDB parity hex dump of VDEL
      IF(IS.EQ.1 .AND. (IBL.EQ.5 .OR. IBL.EQ.63
     & .OR. IBL.EQ.79)) THEN
       WRITE(0,'(A,I3,A,Z8,A,Z8,A,Z8,A,Z8,A,Z8,A,Z8)')
     &  'F_VS IBL=',IBL,
     &  ' V24=',TRANSFER(VS2(2,4),1),
     &  ' V23=',TRANSFER(VS2(2,3),1),
     &  ' V14=',TRANSFER(VS1(2,4),1),
     &  ' V13=',TRANSFER(VS1(2,3),1),
     &  ' DUE1=',TRANSFER(DUE1,1),
     &  ' DDS1=',TRANSFER(DDS1,1)
       IF(IBL.EQ.79) THEN
        WRITE(0,'(A,Z8,A,Z8,A,Z8,A,Z8,A,Z8,A,Z8)')
     &   'F_VS79R1 V114=',TRANSFER(VS1(1,4),1),
     &   ' V113=',TRANSFER(VS1(1,3),1),
     &   ' V214=',TRANSFER(VS2(1,4),1),
     &   ' V213=',TRANSFER(VS2(1,3),1),
     &   ' DUE2=',TRANSFER(DUE2,1),
     &   ' VSREZ1=',TRANSFER(VSREZ(1),1)
        WRITE(0,'(A,I2,A,Z8,A,Z8,A,Z8)')
     &   'F_VSX79 IS=',IS,' VSX1=',TRANSFER(VSX(1),1),
     &   ' VSX2=',TRANSFER(VSX(2),1),
     &   ' VSX3=',TRANSFER(VSX(3),1)
       ENDIF
      ENDIF
      WRITE(0,'(A,I2,A,I3,A,Z8,A,Z8,A,Z8)')
     & 'F_VDEL  IS=',IS,' IBL=',IBL,
     & ' V1=',TRANSFER(VDEL(1,1,IV),1),
     & ' V2=',TRANSFER(VDEL(2,1,IV),1),
     & ' V3=',TRANSFER(VDEL(3,1,IV),1)
C
C---- trace VB at wake stations for parity check
      IF(IS.EQ.2 .AND. IBL.EQ.85) THEN
       WRITE(0,'(A,I4,A,Z8,A,Z8,A,Z8)')
     &  'F_WVB85 IV=',IV,
     &  ' vb12=',TRANSFER(VB(1,2,IV),1),
     &  ' va12=',TRANSFER(VA(1,2,IV),1),
     &  ' wake=',TRANSFER(MERGE(1,0,WAKE),1)
      ENDIF
      IF(IBL.EQ.IBLTE(IS)+1) THEN
C
C----- redefine coefficients for TTE, DTE, etc
       VZ(1,1)    = VS1(1,1)*CTE_CTE1
       VZ(1,2)    = VS1(1,1)*CTE_TTE1 + VS1(1,2)*TTE_TTE1
       VB(1,1,IV) = VS1(1,1)*CTE_CTE2
       VB(1,2,IV) = VS1(1,1)*CTE_TTE2 + VS1(1,2)*TTE_TTE2
C
       VZ(2,1)    = VS1(2,1)*CTE_CTE1
       VZ(2,2)    = VS1(2,1)*CTE_TTE1 + VS1(2,2)*TTE_TTE1
       VB(2,1,IV) = VS1(2,1)*CTE_CTE2
       VB(2,2,IV) = VS1(2,1)*CTE_TTE2 + VS1(2,2)*TTE_TTE2
C
       VZ(3,1)    = VS1(3,1)*CTE_CTE1
       VZ(3,2)    = VS1(3,1)*CTE_TTE1 + VS1(3,2)*TTE_TTE1
       VB(3,1,IV) = VS1(3,1)*CTE_CTE2
       VB(3,2,IV) = VS1(3,1)*CTE_TTE2 + VS1(3,2)*TTE_TTE2
C
      ENDIF
C
C---- turbulent intervals will follow if currently at transition interval
      IF(TRAN) THEN
        TURB = .TRUE.
C
C------ save transition location
        ITRAN(IS) = IBL
        TFORCE(IS) = TRFORC
        XSSITR(IS) = XT
C
C------ interpolate airfoil geometry to find transition x/c
C-      (for user output)
        IF(IS.EQ.1) THEN
         STR = SST - XT
        ELSE
         STR = SST + XT
        ENDIF
        CHX = XTE - XLE
        CHY = YTE - YLE
        CHSQ = CHX**2 + CHY**2
        XTR = SEVAL(STR,X,XP,S,N)
        YTR = SEVAL(STR,Y,YP,S,N)
        XOCTR(IS) = ((XTR-XLE)*CHX + (YTR-YLE)*CHY)/CHSQ
        YOCTR(IS) = ((YTR-YLE)*CHX - (XTR-XLE)*CHY)/CHSQ
      ENDIF
C
      TRAN = .FALSE.
C
      IF(IBL.EQ.IBLTE(IS)) THEN
C----- set "2" variables at TE to wake correlations for next station
C
       TURB = .TRUE.
       WAKE = .TRUE.
       CALL BLVAR(3)
       CALL BLMID(3)
      ENDIF
C
      DO 80 JS=1, 2
        DO 810 JBL=2, NBL(JS)
          JV = ISYS(JBL,JS)
          U1_M(JV) = U2_M(JV)
          D1_M(JV) = D2_M(JV)
  810   CONTINUE
   80 CONTINUE
C
      U1_A = U2_A
      D1_A = D2_A
C
      DUE1 = DUE2
      DDS1 = DDS2
C      
C---- set BL variables for next station
      DO 190 ICOM=1, NCOM
        COM1(ICOM) = COM2(ICOM)
  190 CONTINUE
C
C---- next streamwise station
 1000 CONTINUE
C
      IF(TFORCE(IS)) THEN
       WRITE(*,9100) IS,XOCTR(IS),ITRAN(IS)
 9100  FORMAT(1X,'Side',I2,' forced transition at x/c = ',F7.4,I5)
      ELSE
       WRITE(*,9200) IS,XOCTR(IS),ITRAN(IS)
 9200  FORMAT(1X,'Side',I2,'  free  transition at x/c = ',F7.4,I5)
      ENDIF
C
C---- next airfoil side
 2000 CONTINUE
C
      IF(SETBL_COUNT.EQ.33) THEN
       WRITE(0,'(A,3(1X,A,Z8.8))') 'F_VDEL160_END',
     &  'V11=',TRANSFER(VDEL(1,1,160),1),
     &  'V21=',TRANSFER(VDEL(2,1,160),1),
     &  'V31=',TRANSFER(VDEL(3,1,160),1)
      ENDIF
      RETURN
      END


      SUBROUTINE IBLSYS
C---------------------------------------------
C     Sets the BL Newton system line number
C     corresponding to each BL station.
C---------------------------------------------
      INCLUDE 'XFOIL.INC'
      INCLUDE 'XBL.INC'
C
      IV = 0
      DO 10 IS=1, 2
        DO 110 IBL=2, NBL(IS)
          IV = IV+1
          ISYS(IBL,IS) = IV
  110   CONTINUE
   10 CONTINUE
C
      NSYS = IV
      WRITE(0,'(A,I4,A,I4,A,I4)') 'F_IBLSYS_CALL NSYS=', NSYS,
     & ' NBL1=', NBL(1), ' NBL2=', NBL(2)
      IF(NSYS.GT.2*IVX) STOP '*** IBLSYS: BL system array overflow. ***'
C
      RETURN
      END


      SUBROUTINE MRCHUE
C----------------------------------------------------
C     Marches the BLs and wake in direct mode using
C     the UEDG array. If separation is encountered,
C     a plausible value of Hk extrapolated from
C     upstream is prescribed instead.  Continuous
C     checking of transition onset is performed.
C----------------------------------------------------
      INCLUDE 'XFOIL.INC'
      INCLUDE 'XBL.INC'
      LOGICAL DIRECT
      REAL MSQ
C
C---- shape parameters for separation criteria
      HLMAX = 3.8
      HTMAX = 2.5
      WRITE(0,'(A)') 'F_MRCHUE_ENTRY'
C
      DO 2000 IS=1, 2
C
      WRITE(*,*) '   side ', IS, ' ...'
      IF(IS.EQ.2) THEN
        WRITE(0,'(A,I4,A,I4)')
     &   'F_MRCHUE_IBLTE2=',IBLTE(2),' NBL2=',NBL(2)
      ENDIF
C
C---- set forced transition arc length position
      CALL XIFSET(IS)
C
C---- initialize similarity station with Thwaites' formula
      IBL = 2
      XSI = XSSI(IBL,IS)
      UEI = UEDG(IBL,IS)
C      BULE = LOG(UEDG(IBL+1,IS)/UEI) / LOG(XSSI(IBL+1,IS)/XSI)
C      BULE = MAX( -.08 , BULE )
      BULE = 1.0
      UCON = UEI/XSI**BULE
      TSQ = 0.45/(UCON*(5.0*BULE+1.0)*REYBL) * XSI**(1.0-BULE)
      THI = SQRT(TSQ)
      DSI = 2.2*THI
      IF(IS.EQ.1) THEN
       WRITE(0,'(A,Z8.8,1X,Z8.8,1X,Z8.8,1X,Z8.8,1X,Z8.8,1X,Z8.8)')
     &  'F_THWAITES',
     &  TRANSFER(THI,1),TRANSFER(DSI,1),
     &  TRANSFER(TSQ,1),TRANSFER(XSI,1),
     &  TRANSFER(UEI,1),TRANSFER(REYBL,1)
      ENDIF
      AMI = 0.0
C
C---- initialize Ctau for first turbulent station
      CTI = 0.03
C
      TRAN = .FALSE.
      TURB = .FALSE.
      ITRAN(IS) = IBLTE(IS)
C
C---- march downstream
      DO 1000 IBL=2, NBL(IS)
        IBM = IBL-1
C
        IW = IBL - IBLTE(IS)
C
        SIMI = IBL.EQ.2
        WAKE = IBL.GT.IBLTE(IS)
C
C------ prescribed quantities
        XSI = XSSI(IBL,IS)
        UEI = UEDG(IBL,IS)
C
        IF(WAKE) THEN
         IW = IBL - IBLTE(IS)
         DSWAKI = WGAP(IW)
        ELSE
         DSWAKI = 0.
        ENDIF
C
        DIRECT = .TRUE.
C
C------ (removed - was in wrong function scope)
C------ trace seed before Newton loop at s2 stn 4
        IF(IS.EQ.2 .AND. IBL.EQ.4) THEN
         WRITE(*,'(A,Z8,1X,Z8,1X,Z8,1X,Z8)')
     &    'F_MUE_SEED24',
     &    TRANSFER(THI,1),TRANSFER(DSI,1),
     &    TRANSFER(UEI,1),TRANSFER(CTI,1)
        ENDIF
        IF(IS.EQ.2 .AND. IBL.EQ.5) THEN
         WRITE(0,'(A,Z8,A,Z8,A,Z8,A,Z8,A,Z8)')
     &    'F_MUE_SEED25 CTI=',TRANSFER(CTI,1),
     &    ' AMI=',TRANSFER(AMI,1),
     &    ' THI=',TRANSFER(THI,1),
     &    ' DSI=',TRANSFER(DSI,1),
     &    ' UEI=',TRANSFER(UEI,1)
        ENDIF
C------ Newton iteration loop for current station
        DO 100 ITBL=1, 25
          IF(IS.EQ.1 .AND. IBL.EQ.95) THEN
           WRITE(0,'(A,I2,A,Z8,A,Z8,A,Z8,A,Z8,A,Z8,A,Z8,A,L1)')
     &      'F_MUE95 it=',ITBL,
     &      ' T=',TRANSFER(THI,1),
     &      ' D=',TRANSFER(DSI,1),
     &      ' U=',TRANSFER(UEI,1),
     &      ' CTI=',TRANSFER(CTI,1),
     &      ' HK=',TRANSFER(HK2,1),
     &      ' HS=',TRANSFER(HS2,1),
     &      ' dir=',DIRECT
          ENDIF
          IF(IS.EQ.2 .AND. IBL.EQ.90) THEN
           WRITE(0,'(A,I2,6(A,Z8),A,L1)')
     &      'F_WMUE90 it=',ITBL,
     &      ' T=',TRANSFER(THI,1),
     &      ' D=',TRANSFER(DSI,1),
     &      ' U=',TRANSFER(UEI,1),
     &      ' CTI=',TRANSFER(CTI,1),
     &      ' T1=',TRANSFER(THET(IBM,IS),1),
     &      ' D1=',TRANSFER(DSTR(IBM,IS),1),
     &      ' dir=',DIRECT
          ENDIF
          IF(IS.EQ.2 .AND. IBL.EQ.67) THEN
           WRITE(0,'(A,I2,6(A,Z8),A,L1)')
     &      'F_WMUE67 it=',ITBL,
     &      ' T=',TRANSFER(THI,1),
     &      ' D=',TRANSFER(DSI,1),
     &      ' U=',TRANSFER(UEI,1),
     &      ' CTI=',TRANSFER(CTI,1),
     &      ' T1=',TRANSFER(THET(IBM,IS),1),
     &      ' D1=',TRANSFER(DSTR(IBM,IS),1),
     &      ' dir=',DIRECT
          ENDIF
          IF(IS.EQ.2 .AND. IBL.EQ.65) THEN
           WRITE(0,'(A,I2,6(A,Z8),A,L1)')
     &      'F_WMUE65 it=',ITBL,
     &      ' T=',TRANSFER(THI,1),
     &      ' D=',TRANSFER(DSI,1),
     &      ' U=',TRANSFER(UEI,1),
     &      ' CTI=',TRANSFER(CTI,1),
     &      ' T1=',TRANSFER(THET(IBM,IS),1),
     &      ' D1=',TRANSFER(DSTR(IBM,IS),1),
     &      ' dir=',DIRECT
          ENDIF
          IF(IS.EQ.2 .AND. IBL.EQ.68) THEN
           WRITE(0,'(A,I2,A,Z8,A,Z8,A,Z8,A,Z8,A,Z8,A,Z8)')
     &      'F_MUE68 it=',ITBL,
     &      ' T=',TRANSFER(THI,1),
     &      ' D=',TRANSFER(DSI,1),
     &      ' U=',TRANSFER(UEI,1),
     &      ' CTI=',TRANSFER(CTI,1),
     &      ' T1=',TRANSFER(THET(IBM,IS),1),
     &      ' D1=',TRANSFER(DSTR(IBM,IS),1)
           WRITE(0,'(A,I2,6(A,Z8))')
     &      'F_MUE68_COM1 it=',ITBL,
     &      ' HK1=',TRANSFER(HK1,1),
     &      ' HS1=',TRANSFER(HS1,1),
     &      ' CF1=',TRANSFER(CF1,1),
     &      ' DI1=',TRANSFER(DI1,1),
     &      ' CQ1=',TRANSFER(CQ1,1),
     &      ' US1=',TRANSFER(US1,1)
          ENDIF
C
C-------- assemble 10x3 linearized system for dCtau, dTh, dDs, dUe, dXi
C         at the previous "1" station and the current "2" station
C         (the "1" station coefficients will be ignored)
C
C
          CALL BLPRV(XSI,AMI,CTI,THI,DSI,DSWAKI,UEI)
          CALL BLKIN
C-------- trace MRCHUE station 3 side 2 iteration 1
          IF(IS.EQ.2 .AND. IBL.EQ.3 .AND. ITBL.EQ.1) THEN
           WRITE(0,'(A,Z8,A,Z8,A,Z8,A,Z8,A,Z8,A,Z8)')
     &      'F_MUE3_KIN T2=',TRANSFER(T2,1),
     &      ' D2=',TRANSFER(D2,1),
     &      ' U2=',TRANSFER(U2,1),
     &      ' HK2=',TRANSFER(HK2,1),
     &      ' RT2=',TRANSFER(RT2,1),
     &      ' S2=',TRANSFER(S2,1)
           WRITE(0,'(A,Z8,A,Z8,A,Z8,A,Z8,A,Z8)')
     &      'F_MUE3_COM T1=',TRANSFER(T1,1),
     &      ' D1=',TRANSFER(D1,1),
     &      ' U1=',TRANSFER(U1,1),
     &      ' HK1=',TRANSFER(HK1,1),
     &      ' RT1=',TRANSFER(RT1,1)
          ENDIF
C-------- trace MRCHUE station 5 side 2 detailed
          IF(IS.EQ.2 .AND. IBL.EQ.5 .AND. ITBL.LE.3) THEN
           WRITE(0,'(A,I2,A,Z8,A,Z8,A,Z8,A,Z8,A,Z8)')
     &      'F_MUE5 it=',ITBL,
     &      ' T2=',TRANSFER(T2,1),
     &      ' D2=',TRANSFER(D2,1),
     &      ' U2=',TRANSFER(U2,1),
     &      ' HK2=',TRANSFER(HK2,1),
     &      ' RT2=',TRANSFER(RT2,1)
           WRITE(0,'(A,I2,A,Z8,A,Z8,A,Z8,A,Z8,A,Z8)')
     &      'F_MUE5b it=',ITBL,
     &      ' T1=',TRANSFER(T1,1),
     &      ' D1=',TRANSFER(D1,1),
     &      ' U1=',TRANSFER(U1,1),
     &      ' HK1=',TRANSFER(HK1,1),
     &      ' RT1=',TRANSFER(RT1,1)
           WRITE(0,'(A,I2,A,Z8,A,Z8,A,Z8,A,Z8)')
     &      'F_MUE5c it=',ITBL,
     &      ' X1=',TRANSFER(X1,1),
     &      ' X2=',TRANSFER(X2,1),
     &      ' AMI=',TRANSFER(AMI,1),
     &      ' THI=',TRANSFER(THI,1)
          ENDIF
C
C-------- trace MRCHUE BLKIN at side 2 station 83
          IF(IS.EQ.2 .AND. IBL.EQ.83) THEN
           WRITE(0,'(A,I2,A,Z8,A,Z8,A,Z8,A,Z8,A,Z8)')
     &      'F_MUE83_KIN it=',ITBL,
     &      ' HK=',TRANSFER(HK2,1),
     &      ' RT=',TRANSFER(RT2,1),
     &      ' M2=',TRANSFER(M2,1),
     &      ' H2=',TRANSFER(H2,1),
     &      ' U2=',TRANSFER(U2,1)
          ENDIF
C-------- trace MRCHUE BLKIN at side 2 station 4
          IF(IS.EQ.2 .AND. IBL.EQ.4 .AND. ITBL.EQ.1) THEN
           WRITE(*,'(A,Z8,1X,Z8,1X,Z8,1X,Z8,1X,Z8,1X,Z8)')
     &      'F_MUE_BK24',
     &      TRANSFER(HK2,1),TRANSFER(RT2,1),
     &      TRANSFER(H2,1),TRANSFER(HS2,1),
     &      TRANSFER(THI,1),TRANSFER(DSI,1)
           WRITE(*,'(A,Z8,1X,Z8,1X,Z8,1X,Z8,1X,Z8)')
     &      'F_MUE_BK24_prev',
     &      TRANSFER(X1,1),TRANSFER(X2,1),
     &      TRANSFER(T1,1),TRANSFER(T2,1),
     &      TRANSFER(D1,1)
          ENDIF
C
C-------- check for transition and set appropriate flags and things
          IF((.NOT.SIMI) .AND. (.NOT.TURB)) THEN
           CALL TRCHEK
           AMI = AMPL2
C---- GDB: dump accumulated N and transition at laminar stations
           IF((IS.EQ.2 .AND. IBL.GE.3 .AND. IBL.LE.8) .OR.
     &        (IS.EQ.1 .AND. IBL.GE.8 .AND. IBL.LE.14)) THEN
             WRITE(0,'(A,I1,A,I3,A,I2,A,Z8,A,L1,A,Z8,A,Z8)')
     &        'F_AMPL S=',IS,' I=',IBL,' it=',ITBL,
     &        ' N=',TRANSFER(AMI,1),
     &        ' TR=',TRAN,
     &        ' HK=',TRANSFER(HK2,1),
     &        ' T2=',TRANSFER(T2,1)
           ENDIF
C---- usa48 trace: AMPL evolution at ibl 23..27 side 1
           IF(IS.EQ.1 .AND. IBL.GE.23 .AND. IBL.LE.27) THEN
             WRITE(0,'(A,I1,A,I3,A,I2,A,Z8,A,L1,A,Z8,A,Z8,A,Z8,A,Z8)')
     &        'F_USA48_AMPL S=',IS,' I=',IBL,' it=',ITBL,
     &        ' N=',TRANSFER(AMI,1),
     &        ' TR=',TRAN,
     &        ' HK=',TRANSFER(HK2,1),
     &        ' T2=',TRANSFER(T2,1),
     &        ' D2=',TRANSFER(D2,1),
     &        ' U2=',TRANSFER(U2,1)
           ENDIF
C
           IF(TRAN) THEN
            ITRAN(IS) = IBL
            IF(CTI.LE.0.0) THEN
             CTI = 0.03
             S2 = CTI
            ENDIF
           ELSE
            ITRAN(IS) = IBL+2
           ENDIF
C
C
          ENDIF
C
          IF(IBL.EQ.IBLTE(IS)+1) THEN
           TTE = THET(IBLTE(1),1) + THET(IBLTE(2),2)
           DTE = DSTR(IBLTE(1),1) + DSTR(IBLTE(2),2) + ANTE
           CTE = ( CTAU(IBLTE(1),1)*THET(IBLTE(1),1)
     &           + CTAU(IBLTE(2),2)*THET(IBLTE(2),2) ) / TTE
          CALL TESYS(CTE,TTE,DTE)
C---- GDB: dump TESYS inputs at TE junction side 2
          IF(IS.EQ.2 .AND. ITBL.EQ.1) THEN
            WRITE(0,'(A,Z8,A,Z8,A,Z8,A,Z8,A,Z8,A,Z8)')
     &       'F_TE tte=',TRANSFER(TTE,1),
     &       ' dte=',TRANSFER(DTE,1),
     &       ' cte=',TRANSFER(CTE,1),
     &       ' t=',TRANSFER(THI,1),
     &       ' d=',TRANSFER(DSI,1),
     &       ' s=',TRANSFER(CTI,1)
          ENDIF
          ELSE
          IF(IS.EQ.2 .AND. IBL.EQ.83 .AND. ITBL.EQ.1) THEN
           WRITE(0,'(A,7(1X,Z8))')
     &      'F_COM1_83',
     &      TRANSFER(HK1,1),TRANSFER(RT1,1),
     &      TRANSFER(CF1,1),TRANSFER(DI1,1),
     &      TRANSFER(HS1,1),TRANSFER(US1,1),
     &      TRANSFER(CQ1,1)
           WRITE(0,'(A,6(1X,Z8))')
     &      'F_IN83',
     &      TRANSFER(T1,1),TRANSFER(D1,1),
     &      TRANSFER(U1,1),TRANSFER(T2,1),
     &      TRANSFER(D2,1),TRANSFER(U2,1)
          ENDIF
           TRACE_SIDE = IS
           TRACE_STATION = IBL
           TRACE_ITER = ITBL
           CALL BLSYS(2)
          ENDIF
C---- NACA 1408 debug: station 71 POST-BLSYS COM state (every iter)
          IF(IS.EQ.2 .AND. IBL.EQ.71) THEN
            WRITE(0,'(A,I2,9(A,Z8))')
     &       'F_MUE71PB it=',ITBL,
     &       ' HK2=',TRANSFER(HK2,1),
     &       ' HK1=',TRANSFER(HK1,1),
     &       ' CF2=',TRANSFER(CF2,1),
     &       ' CF1=',TRANSFER(CF1,1),
     &       ' DI2=',TRANSFER(DI2,1),
     &       ' DI1=',TRANSFER(DI1,1),
     &       ' HS2=',TRANSFER(HS2,1),
     &       ' HS1=',TRANSFER(HS1,1),
     &       ' US2=',TRANSFER(US2,1)
            WRITE(0,'(A,I2,5(A,Z8))')
     &       'F_MUE71PB_STATE it=',ITBL,
     &       ' T2=',TRANSFER(T2,1),
     &       ' D2=',TRANSFER(D2,1),
     &       ' U2=',TRANSFER(U2,1),
     &       ' T1=',TRANSFER(T1,1),
     &       ' D1=',TRANSFER(D1,1)
          ENDIF
          IF(IS.EQ.2 .AND. IBL.GE.82 .AND. IBL.LE.85
     &       .AND. ITBL.EQ.1) THEN
           WRITE(0,'(A,I1,A,I4,A,I1,3(A,Z8),12(A,Z8))')
     &      'F_MRCHDU s=',IS,' i=',IBL,' it=',ITBL-1,
     &      ' R1=',TRANSFER(VSREZ(1),1),
     &      ' R2=',TRANSFER(VSREZ(2),1),
     &      ' R3=',TRANSFER(VSREZ(3),1),
     &      ' V11=',TRANSFER(VS2(1,1),1),
     &      ' V12=',TRANSFER(VS2(1,2),1),
     &      ' V13=',TRANSFER(VS2(1,3),1),
     &      ' V14=',TRANSFER(VS2(1,4),1),
     &      ' V21=',TRANSFER(VS2(2,1),1),
     &      ' V22=',TRANSFER(VS2(2,2),1),
     &      ' V23=',TRANSFER(VS2(2,3),1),
     &      ' V24=',TRANSFER(VS2(2,4),1),
     &      ' V31=',TRANSFER(VS2(3,1),1),
     &      ' V32=',TRANSFER(VS2(3,2),1),
     &      ' V33=',TRANSFER(VS2(3,3),1),
     &      ' V34=',TRANSFER(VS2(3,4),1)
          ENDIF
C---- moved trace to after update
          IF(IS.EQ.2 .AND. IBL.EQ.5 .AND. ITBL.LE.3) THEN
           WRITE(0,'(A,I2,A,Z8,A,Z8,A,Z8,A,Z8,A,Z8)')
     &      'F_MUE5_SEC1 it=',ITBL,
     &      ' CF1=',TRANSFER(CF1,1),
     &      ' HS1=',TRANSFER(HS1,1),
     &      ' DI1=',TRANSFER(DI1,1),
     &      ' US1=',TRANSFER(US1,1),
     &      ' CQ1=',TRANSFER(CQ1,1)
           WRITE(0,'(A,I2,A,Z8,A,Z8,A,Z8,A,Z8,A,Z8)')
     &      'F_MUE5_SEC2 it=',ITBL,
     &      ' CF2=',TRANSFER(CF2,1),
     &      ' HS2=',TRANSFER(HS2,1),
     &      ' DI2=',TRANSFER(DI2,1),
     &      ' US2=',TRANSFER(US2,1),
     &      ' CQ2=',TRANSFER(CQ2,1)
          ENDIF
          IF(IS.EQ.2 .AND. IBL.EQ.4 .AND. ITBL.LE.25) THEN
           WRITE(*,'(A,I2,1X,L1,1X,Z8,1X,Z8,1X,Z8,1X,Z8,1X,Z8)')
     &      'F_MUE_RES24',ITBL,DIRECT,
     &      TRANSFER(VSREZ(1),1),
     &      TRANSFER(VSREZ(2),1),
     &      TRANSFER(VSREZ(3),1),
     &      TRANSFER(THI,1),TRANSFER(DSI,1)
          ENDIF
          IF(IS.EQ.2 .AND. IBL.EQ.83 .AND. ITBL.EQ.1) THEN
           WRITE(0,'(A,A,Z8,A,Z8,A,Z8,A,Z8,A,Z8,A,Z8,A,Z8)')
     &      'F_MUE83_BLV',
     &      ' HK=',TRANSFER(HK2,1),
     &      ' RT=',TRANSFER(RT2,1),
     &      ' HS=',TRANSFER(HS2,1),
     &      ' CF=',TRANSFER(CF2,1),
     &      ' DI=',TRANSFER(DI2,1),
     &      ' US=',TRANSFER(US2,1),
     &      ' CQ=',TRANSFER(CQ2,1)
           WRITE(0,'(A,Z8.8,1X,Z8.8,1X,Z8.8,1X,Z8.8)')
     &      'F_MUE83_RES ',
     &      TRANSFER(VSREZ(1),1),TRANSFER(VSREZ(2),1),
     &      TRANSFER(VSREZ(3),1),TRANSFER(VSREZ(4),1)
           DO 8301 JJR=1, 4
             WRITE(0,'(A,I1,A,Z8,1X,Z8,1X,Z8,1X,Z8,A,Z8)')
     &        'F_MUE83_SYS r',JJR-1,':',
     &        TRANSFER(VS2(JJR,1),1),
     &        TRANSFER(VS2(JJR,2),1),
     &        TRANSFER(VS2(JJR,3),1),
     &        TRANSFER(VS2(JJR,4),1),
     &        ' |',TRANSFER(VSREZ(JJR),1)
 8301      CONTINUE
           WRITE(0,'(A,Z8.8,1X,Z8.8,1X,Z8.8,1X,Z8.8,
     &      1X,Z8.8,1X,Z8.8)')
     &      'F_MUE83_COM1 ',
     &      TRANSFER(HK1,1),TRANSFER(CF1,1),
     &      TRANSFER(DI1,1),TRANSFER(HS1,1),
     &      TRANSFER(US1,1),TRANSFER(CQ1,1)
          ENDIF
          IF(IS.EQ.1 .AND. IBL.EQ.58) THEN
            WRITE(*,'(A,I2,A,Z8,A,Z8,A,Z8,A,Z8,A,Z8,A,Z8,A,Z8)')
     &       'F_M58 it=',ITBL,
     &       ' T=',TRANSFER(THI,1),
     &       ' D=',TRANSFER(DSI,1),
     &       ' HK=',TRANSFER(HK2,1),
     &       ' R1=',TRANSFER(VSREZ(1),1),
     &       ' R2=',TRANSFER(VSREZ(2),1),
     &       ' R3=',TRANSFER(VSREZ(3),1),
     &       ' AM=',TRANSFER(AMI,1)
          ENDIF
          IF(IS.EQ.1 .AND. (IBL.EQ.58.OR.IBL.EQ.59)
     &       .AND. ITBL.LE.2) THEN
            DO 9793 JJR=1, 4
              WRITE(*,'(A,I2,A,I2,A,I1,A,Z8,1X,Z8,1X,Z8,1X,Z8,A,Z8)')
     &         'F_MUE',IBL,'i',ITBL,' r',JJR-1,':',
     &         TRANSFER(VS2(JJR,1),1),
     &         TRANSFER(VS2(JJR,2),1),
     &         TRANSFER(VS2(JJR,3),1),
     &         TRANSFER(VS2(JJR,4),1),
     &         ' |',TRANSFER(VSREZ(JJR),1)
 9793       CONTINUE
          ENDIF
C---- GDB: dump BLSYS result at station 30 side 1 iters 1,6
          IF(IS.EQ.1 .AND. IBL.EQ.97
     &       .AND. (ITBL.EQ.1.OR.ITBL.EQ.1)) THEN
            WRITE(0,'(A,Z8,A,Z8,A,Z8,A,Z8,A,Z8,A,Z8,A,Z8)')
     &       'F_BLSYS30 HK2=',TRANSFER(HK2,1),
     &       ' HS2=',TRANSFER(HS2,1),
     &       ' CF2=',TRANSFER(CF2,1),
     &       ' DI2=',TRANSFER(DI2,1),
     &       ' R1=',TRANSFER(VSREZ(1),1),
     &       ' R2=',TRANSFER(VSREZ(2),1),
     &       ' R3=',TRANSFER(VSREZ(3),1)
          ENDIF
C
          IF(DIRECT) THEN
C
C--------- try direct mode (set dUe = 0 in currently empty 4th line)
           VS2(4,1) = 0.
           VS2(4,2) = 0.
           VS2(4,3) = 0.
           VS2(4,4) = 1.0
           VSREZ(4) = 0.
           IF(IS.EQ.2 .AND. IBL.EQ.85 .AND. ITBL.LE.6) THEN
            DO 9785 JJR=1, 4
             WRITE(0,'(A,I2,A,I1,A,Z8,1X,Z8,1X,Z8,1X,Z8,A,Z8)')
     &        'F_MUE85_SYS it=',ITBL,' r',JJR-1,':',
     &        TRANSFER(VS2(JJR,1),1),
     &        TRANSFER(VS2(JJR,2),1),
     &        TRANSFER(VS2(JJR,3),1),
     &        TRANSFER(VS2(JJR,4),1),
     &        ' |',TRANSFER(VSREZ(JJR),1)
 9785       CONTINUE
           ENDIF
C
C--------- GDB: dump COM2 seed + residuals at station 51 iter 1
           IF(IS.EQ.2 .AND. IBL.EQ.51 .AND. ITBL.EQ.1) THEN
            WRITE(0,'(A,Z8,A,Z8,A,Z8,A,Z8,A,Z8,A,Z8)')
     &       'F_SEED_S51 cti=',TRANSFER(CTI,1),
     &       ' thi=',TRANSFER(THI,1),
     &       ' dsi=',TRANSFER(DSI,1),
     &       ' uei=',TRANSFER(UEI,1),
     &       ' r0=',TRANSFER(VSREZ(1),1),
     &       ' r1=',TRANSFER(VSREZ(2),1)
           ENDIF
C---- GDB: dump VS2(1,3) at GAUSS entry for station 58 ALL iters
           IF(IS.EQ.1 .AND. IBL.EQ.97) THEN
             WRITE(0,'(A,I2,A,Z8)')
     &        'F_GAUSS_V13 it=',ITBL,
     &        ' v13=',TRANSFER(VS2(1,3),1)
           ENDIF
C---- GDB: dump full laminar system at station 58 iter 1
           IF(IS.EQ.1 .AND. IBL.EQ.97 .AND. ITBL.EQ.1) THEN
             DO 9351 JJR=1, 3
               WRITE(0,'(A,I1,A,Z8,1X,Z8,1X,Z8,1X,Z8,A,Z8)')
     &          'F_LAM58 r',JJR-1,':',
     &          TRANSFER(VS2(JJR,1),1),
     &          TRANSFER(VS2(JJR,2),1),
     &          TRANSFER(VS2(JJR,3),1),
     &          TRANSFER(VS2(JJR,4),1),
     &          ' |',TRANSFER(VSREZ(JJR),1)
 9351        CONTINUE
           ENDIF
           IF(IS.EQ.2 .AND. IBL.EQ.58) THEN
            WRITE(0,'(A,I2,A,Z8,A,Z8,A,Z8,A,Z8)')
     &       'F_MRCHUE58_RHS it=',ITBL,
     &       ' R0=',TRANSFER(VSREZ(1),1),
     &       ' R1=',TRANSFER(VSREZ(2),1),
     &       ' R2=',TRANSFER(VSREZ(3),1),
     &       ' R3=',TRANSFER(VSREZ(4),1)
           ENDIF
           IF(IS.EQ.1 .AND. IBL.EQ.58 .AND. ITBL.EQ.4) THEN
             DO 9791 JJR=1, 4
               WRITE(*,'(A,I1,A,Z8,1X,Z8,1X,Z8,1X,Z8,A,Z8)')
     &          'F_DIR58_4 r',JJR-1,':',
     &          TRANSFER(VS2(JJR,1),1),
     &          TRANSFER(VS2(JJR,2),1),
     &          TRANSFER(VS2(JJR,3),1),
     &          TRANSFER(VS2(JJR,4),1),
     &          ' |',TRANSFER(VSREZ(JJR),1)
 9791        CONTINUE
           ENDIF
C--------- solve Newton system for current "2" station
           IF(IS.EQ.2 .AND. IBL.EQ.5 .AND. ITBL.LE.3) THEN
             WRITE(0,'(A,I2,A,Z8,A,Z8,A,Z8,A,Z8)')
     &        'F_MUE5_RHS it=',ITBL,
     &        ' R0=',TRANSFER(VSREZ(1),1),
     &        ' R1=',TRANSFER(VSREZ(2),1),
     &        ' R2=',TRANSFER(VSREZ(3),1),
     &        ' R3=',TRANSFER(VSREZ(4),1)
           ENDIF
C---- NACA 1408 Re=5M a=2 Nc=12 debug: station 71 DMAX and delta
           IF(IS.EQ.2 .AND. IBL.EQ.71) THEN
            WRITE(0,'(A,I2,A,Z8,A,Z8,A,Z8,A,Z8,A,Z8)')
     &       'F_MUE71_DEL it=',ITBL,
     &       ' DM=',TRANSFER(DMAX,1),
     &       ' RL=',TRANSFER(RLX,1),
     &       ' d1=',TRANSFER(VSREZ(1),1),
     &       ' d2=',TRANSFER(VSREZ(2),1),
     &       ' d3=',TRANSFER(VSREZ(3),1)
           ENDIF
C---- NACA 1408 Re=5M a=2 Nc=12 debug: station 71 iter state
           IF(IS.EQ.2 .AND. IBL.EQ.71) THEN
            WRITE(0,'(A,I2,A,L1,A,L1,8(A,Z8))')
     &       'F_MUE71END it=',ITBL,' tran=',TRAN,' turb=',TURB,
     &       ' HK2=',TRANSFER(HK2,1),
     &       ' RT2=',TRANSFER(RT2,1),
     &       ' CF2=',TRANSFER(CF2,1),
     &       ' DI2=',TRANSFER(DI2,1),
     &       ' HS2=',TRANSFER(HS2,1),
     &       ' US2=',TRANSFER(US2,1),
     &       ' CQ2=',TRANSFER(CQ2,1),
     &       ' DE2=',TRANSFER(DE2,1)
            WRITE(0,'(A,I2,A,Z8,A,Z8,A,Z8,A,Z8)')
     &       'F_MUE71STATE it=',ITBL,
     &       ' T=',TRANSFER(THI,1),
     &       ' D=',TRANSFER(DSI,1),
     &       ' U=',TRANSFER(UEI,1),
     &       ' CTI=',TRANSFER(CTI,1)
           ENDIF
C---- NACA 1408 Re=5M a=2 Nc=12 debug: first post-transition station
           IF(IS.EQ.2 .AND. IBL.EQ.72 .AND. ITBL.LE.2) THEN
            WRITE(0,'(A,I2,5(A,Z8))')
     &       'F_MUE72 it=',ITBL,
     &       ' T=',TRANSFER(THI,1),
     &       ' D=',TRANSFER(DSI,1),
     &       ' U=',TRANSFER(UEI,1),
     &       ' C=',TRANSFER(CTI,1)
            DO 9673 JJR=1, 4
             WRITE(0,'(A,I2,A,I1,A,Z8,1X,Z8,1X,Z8,1X,Z8,A,Z8)')
     &        'F_MUE72M it=',ITBL,' r',JJR-1,':',
     &        TRANSFER(VS2(JJR,1),1),
     &        TRANSFER(VS2(JJR,2),1),
     &        TRANSFER(VS2(JJR,3),1),
     &        TRANSFER(VS2(JJR,4),1),
     &        ' |',TRANSFER(VSREZ(JJR),1)
 9673       CONTINUE
            WRITE(0,'(A,I2,7(A,Z8))')
     &       'F_MUE72_COM1 it=',ITBL,
     &       ' HK1=',TRANSFER(HK1,1),
     &       ' RT1=',TRANSFER(RT1,1),
     &       ' CF1=',TRANSFER(CF1,1),
     &       ' DI1=',TRANSFER(DI1,1),
     &       ' HS1=',TRANSFER(HS1,1),
     &       ' US1=',TRANSFER(US1,1),
     &       ' CQ1=',TRANSFER(CQ1,1)
           ENDIF
           IF(IS.EQ.2 .AND. IBL.EQ.98) THEN
             DO 9671 JJR=1, 4
              WRITE(0,'(A,I2,A,I1,A,Z8,1X,Z8,1X,Z8,1X,Z8,'//
     &         'A,Z8)')
     &         'F_MUE98 it=',ITBL,' r',JJR-1,':',
     &         TRANSFER(VS2(JJR,1),1),
     &         TRANSFER(VS2(JJR,2),1),
     &         TRANSFER(VS2(JJR,3),1),
     &         TRANSFER(VS2(JJR,4),1),
     &         ' |',TRANSFER(VSREZ(JJR),1)
 9671        CONTINUE
           ENDIF
           IF(IS.EQ.1 .AND. IBL.EQ.11 .AND. ITBL.LE.2) THEN
            DO 8311 JJR=1, 4
              WRITE(0,'(A,I2,A,I1,A,Z8,1X,Z8,1X,Z8,1X,Z8,A,Z8)')
     &         'F_SYS11 it=',ITBL-1,' r',JJR-1,':',
     &         TRANSFER(VS2(JJR,1),1),
     &         TRANSFER(VS2(JJR,2),1),
     &         TRANSFER(VS2(JJR,3),1),
     &         TRANSFER(VS2(JJR,4),1),
     &         ' |',TRANSFER(VSREZ(JJR),1)
 8311       CONTINUE
           ENDIF
           IF(IS.EQ.2 .AND. IBL.EQ.68 .AND. ITBL.EQ.1) THEN
            DO 9811 JJR=1, 4
             WRITE(0,'(A,I1,A,Z8,1X,Z8,1X,Z8,1X,Z8,A,Z8)')
     &        'F_MUE68_M r',JJR-1,':',
     &        TRANSFER(VS2(JJR,1),1),
     &        TRANSFER(VS2(JJR,2),1),
     &        TRANSFER(VS2(JJR,3),1),
     &        TRANSFER(VS2(JJR,4),1),
     &        ' |',TRANSFER(VSREZ(JJR),1)
 9811       CONTINUE
           ENDIF
           IF(IS.EQ.2 .AND. IBL.EQ.90 .AND. ITBL.EQ.3) THEN
            DO 9812 JJR=1, 4
             WRITE(0,'(A,I1,A,Z8,1X,Z8,1X,Z8,1X,Z8,A,Z8)')
     &        'F_MAT90 it=3 r',JJR-1,':',
     &        TRANSFER(VS2(JJR,1),1),
     &        TRANSFER(VS2(JJR,2),1),
     &        TRANSFER(VS2(JJR,3),1),
     &        TRANSFER(VS2(JJR,4),1),
     &        ' |',TRANSFER(VSREZ(JJR),1)
 9812       CONTINUE
           ENDIF
           CALL GAUSS(4,4,VS2,VSREZ,1)
           IF(IS.EQ.1 .AND. IBL.EQ.11 .AND. ITBL.LE.2) THEN
            WRITE(0,'(A,I2,4(1X,Z8.8))')
     &       'F_DLT11 it=',ITBL-1,
     &       TRANSFER(VSREZ(1),1),
     &       TRANSFER(VSREZ(2),1),
     &       TRANSFER(VSREZ(3),1),
     &       TRANSFER(VSREZ(4),1)
           ENDIF
           IF(IS.EQ.2 .AND. IBL.EQ.98) THEN
             WRITE(0,'(A,I2,4(A,Z8),A,Z8,A,Z8)')
     &        'F_MUE98D it=',ITBL,
     &        ' d0=',TRANSFER(VSREZ(1),1),
     &        ' d1=',TRANSFER(VSREZ(2),1),
     &        ' d2=',TRANSFER(VSREZ(3),1),
     &        ' d3=',TRANSFER(VSREZ(4),1),
     &        ' T=',TRANSFER(THI,1),
     &        ' D=',TRANSFER(DSI,1)
           ENDIF
           IF(IS.EQ.2 .AND. IBL.EQ.5 .AND. ITBL.LE.3) THEN
             WRITE(0,'(A,I2,A,Z8,A,Z8,A,Z8,A,Z8)')
     &        'F_MUE5_DLT it=',ITBL,
     &        ' d0=',TRANSFER(VSREZ(1),1),
     &        ' d1=',TRANSFER(VSREZ(2),1),
     &        ' d2=',TRANSFER(VSREZ(3),1),
     &        ' d3=',TRANSFER(VSREZ(4),1)
           ENDIF
           IF(IS.EQ.2 .AND. IBL.EQ.4 .AND.
     &        (ITBL.EQ.8.OR.ITBL.EQ.9)) THEN
            WRITE(*,'(A,I2,1X,Z8,1X,Z8,1X,Z8,1X,Z8)')
     &       'F_DELTA24',ITBL,
     &       TRANSFER(VSREZ(1),1),TRANSFER(VSREZ(2),1),
     &       TRANSFER(VSREZ(3),1),TRANSFER(VSREZ(4),1)
           ENDIF
C  delta dump moved after RLX
C---- GDB: dump delta at station 30 side 1
           IF(IS.EQ.1 .AND. IBL.EQ.97) THEN
             WRITE(0,'(A,I2,A,Z8,A,Z8,A,Z8,A,Z8,A,Z8,A,Z8)')
     &        'F_DELTA30 it=',ITBL,
     &        ' d0=',TRANSFER(VSREZ(1),1),
     &        ' d1=',TRANSFER(VSREZ(2),1),
     &        ' d2=',TRANSFER(VSREZ(3),1),
     &        ' d3=',TRANSFER(VSREZ(4),1),
     &        ' T=',TRANSFER(THI,1),
     &        ' D=',TRANSFER(DSI,1)
           ENDIF
C
C--------- determine max changes and underrelax if necessary
           DMAX = MAX( ABS(VSREZ(2)/THI),
     &                 ABS(VSREZ(3)/DSI)  )
           IF(IBL.LT.ITRAN(IS)) DMAX = MAX(DMAX,ABS(VSREZ(1)/10.0))
           IF(IBL.GE.ITRAN(IS)) DMAX = MAX(DMAX,ABS(VSREZ(1)/CTI ))
C
           RLX = 1.0
           IF(DMAX.GT.0.3) RLX = 0.3/DMAX
C
C---- NACA 1410 Re=500K a=-4 Nc=5: dump UEDG/UINV at wake station entry
           IF(IS.EQ.2 .AND. IBL.GE.93 .AND. IBL.LE.96
     &        .AND. ITBL.EQ.1) THEN
            WRITE(0,'(A,I3,A,Z8,A,Z8,A,Z8,A,Z8)')
     &       'F_WUEDG i=',IBL,
     &       ' UEDG=',TRANSFER(UEDG(IBL,IS),1),
     &       ' UINV=',TRANSFER(UINV(IBL,IS),1),
     &       ' VTI=',TRANSFER(VTI(IBL,IS),1),
     &       ' UEI=',TRANSFER(UEI,1)
           ENDIF
C---- NACA 1410 Re=500K a=-4 Nc=5: wake station 95 side 2 debug
           IF(IS.EQ.2 .AND. IBL.GE.93 .AND. IBL.LE.96) THEN
            WRITE(0,'(A,I3,A,I2,6(A,Z8))')
     &       'F_MUEW i=',IBL,' it=',ITBL,
     &       ' T=',TRANSFER(THI,1),
     &       ' D=',TRANSFER(DSI,1),
     &       ' U=',TRANSFER(UEI,1),
     &       ' C=',TRANSFER(CTI,1),
     &       ' DM=',TRANSFER(DMAX,1),
     &       ' RL=',TRANSFER(RLX,1)
            WRITE(0,'(A,I3,A,I2,4(A,Z8))')
     &       'F_MUEW_D i=',IBL,' it=',ITBL,
     &       ' d0=',TRANSFER(VSREZ(1),1),
     &       ' d1=',TRANSFER(VSREZ(2),1),
     &       ' d2=',TRANSFER(VSREZ(3),1),
     &       ' d3=',TRANSFER(VSREZ(4),1)
           ENDIF
C
           IF(IS.EQ.2 .AND. IBL.GE.85 .AND. IBL.LE.90
     &        .AND. ITBL.LE.6) THEN
            IDIR = 0
            IF(DIRECT) IDIR = 1
            WRITE(0,'(A,I3,A,I2,A,I1,A,Z8,A,Z8,A,Z8,A,Z8,A,Z8,
     &        A,Z8,A,Z8)')
     &       'F_WK8790_PRE I=',IBL,' IT=',ITBL,
     &       ' D=',IDIR,
     &       ' T0=',TRANSFER(THI,1),
     &       ' D0=',TRANSFER(DSI,1),
     &       ' U0=',TRANSFER(UEI,1),
     &       ' C0=',TRANSFER(CTI,1),
     &       ' RL=',TRANSFER(RLX,1),
     &       ' DM=',TRANSFER(DMAX,1),
     &       ' WG=',TRANSFER(DSWAKI,1)
            WRITE(0,'(A,I3,A,I2,A,Z8,A,Z8,A,Z8,A,Z8)')
     &       'F_WK8790_DEL I=',IBL,' IT=',ITBL,
     &       ' d0=',TRANSFER(VSREZ(1),1),
     &       ' d1=',TRANSFER(VSREZ(2),1),
     &       ' d2=',TRANSFER(VSREZ(3),1),
     &       ' d3=',TRANSFER(VSREZ(4),1)
           ENDIF
C
           IF(IS.EQ.1 .AND. IBL.EQ.58) THEN
             WRITE(*,'(A,I2,A,Z8,A,Z8,A,Z8,A,Z8)')
     &        'F_D58 it=',ITBL,
     &        ' d0=',TRANSFER(VSREZ(1),1),
     &        ' d1=',TRANSFER(VSREZ(2),1),
     &        ' d2=',TRANSFER(VSREZ(3),1),
     &        ' rlx=',TRANSFER(RLX,1)
           ENDIF
C--------- see if direct mode is not applicable
           IF(IBL .NE. IBLTE(IS)+1) THEN
C
C---------- calculate resulting kinematic shape parameter Hk
            MSQ = UEI*UEI*HSTINV / (GM1BL*(1.0 - 0.5*UEI*UEI*HSTINV))
            HTEST = (DSI + RLX*VSREZ(3)) / (THI + RLX*VSREZ(2))
            CALL HKIN( HTEST, MSQ, HKTEST, DUMMY, DUMMY)
C
C---------- decide whether to do direct or inverse problem based on Hk
            IF(IBL.LT.ITRAN(IS)) HMAX = HLMAX
            IF(IBL.GE.ITRAN(IS)) HMAX = HTMAX
            DIRECT = HKTEST.LT.HMAX
            IF(IS.EQ.2 .AND. IBL.EQ.4) THEN
             WRITE(*,'(A,I2,L2,1X,Z8,1X,Z8,1X,Z8)')
     &        'F_MODE24',ITBL,DIRECT,
     &        TRANSFER(HKTEST,1),TRANSFER(HMAX,1),
     &        TRANSFER(RLX,1)
            ENDIF
           ENDIF
C
           IF(DIRECT) THEN
C---------- update as usual
ccc            IF(IBL.LT.ITRAN(IS)) AMI = AMI + RLX*VSREZ(1)
            IF(IS.EQ.2 .AND. IBL.EQ.85) THEN
             WRITE(0,'(A,I2,A,Z8,A,Z8,A,Z8,A,Z8,A,Z8,A,Z8)')
     &        'F_MUE85_PRE it=',ITBL,
     &        ' T0=',TRANSFER(THI,1),
     &        ' D0=',TRANSFER(DSI,1),
     &        ' U0=',TRANSFER(UEI,1),
     &        ' C0=',TRANSFER(CTI,1),
     &        ' RL=',TRANSFER(RLX,1),
     &        ' DM=',TRANSFER(DMAX,1)
             WRITE(0,'(A,I2,A,Z8,A,Z8,A,Z8,A,Z8,A,Z8,A,Z8,A,Z8,A,Z8)')
     &        'F_MUE85_DEL it=',ITBL,
     &        ' R0=',TRANSFER(VSREZ(1),1),
     &        ' R1=',TRANSFER(VSREZ(2),1),
     &        ' R2=',TRANSFER(VSREZ(3),1),
     &        ' R3=',TRANSFER(VSREZ(4),1),
     &        ' d0=',TRANSFER(VSREZ(1),1),
     &        ' d1=',TRANSFER(VSREZ(2),1),
     &        ' d2=',TRANSFER(VSREZ(3),1),
     &        ' d3=',TRANSFER(VSREZ(4),1)
            ENDIF
            IF(IBL.GE.ITRAN(IS)) CTI = CTI + RLX*VSREZ(1)
C---- GDB: MRCHUE theta update trace at station 3 side 1
            IF(IS.EQ.1 .AND. IBL.EQ.3) THEN
              WRITE(0,'(A,I2,A,Z8,A,Z8,A,Z8,A,Z8)')
     &         'F_UPD3 it=',ITBL,
     &         ' T_pre=',TRANSFER(THI,1),
     &         ' d1=',TRANSFER(VSREZ(2),1),
     &         ' rlx=',TRANSFER(RLX,1),
     &         ' prod=',TRANSFER(RLX*VSREZ(2),1)
            ENDIF
            IF(IS.EQ.2 .AND. IBL.EQ.58) THEN
              WRITE(0,'(A,I2,A,L1,A,L1,A,Z8,A,Z8,A,Z8,A,Z8,A,Z8,
     &          A,Z8,A,Z8,A,Z8)')
     &         'F_MRCHUE58 it=',ITBL,' D=',DIRECT,' T=',TRAN,
     &         ' D_pre=',TRANSFER(DSI,1),
     &         ' T_pre=',TRANSFER(THI,1),
     &         ' d2=',TRANSFER(VSREZ(3),1),
     &         ' d1=',TRANSFER(VSREZ(2),1),
     &         ' rlx=',TRANSFER(RLX,1),
     &         ' R0=',TRANSFER(VSREZ(1),1),
     &         ' R1=',TRANSFER(VSREZ(2),1),
     &         ' R2=',TRANSFER(VSREZ(3),1)
            ENDIF
            THI = THI + RLX*VSREZ(2)
            IF(IS.EQ.2 .AND. IBL.EQ.3) THEN
              WRITE(0,'(A,I2,A,Z8,A,Z8,A,Z8)')
     &         'F_MUE3 it=',ITBL,
     &         ' T=',TRANSFER(THI,1),
     &         ' d1=',TRANSFER(VSREZ(2),1),
     &         ' rlx=',TRANSFER(RLX,1)
            ENDIF
            IF(IS.EQ.1 .AND. IBL.EQ.3) THEN
              WRITE(0,'(A,I2,A,Z8,A,Z8)')
     &         'F_UPD3 it=',ITBL,
     &         ' T_post=',TRANSFER(THI,1),
     &         ' D_post=',TRANSFER(DSI+RLX*VSREZ(3),1)
            ENDIF
            DSI = DSI + RLX*VSREZ(3)
            IF(IS.EQ.2 .AND. IBL.EQ.58) THEN
              WRITE(0,'(A,I2,A,Z8,A,Z8)')
     &         'F_MRCHUE58 it=',ITBL,
     &         ' D_add=',TRANSFER(DSI,1),
     &         ' T_add=',TRANSFER(THI,1)
            ENDIF
C---- GDB: dump CTAU at wake stations
            IF(WAKE) THEN
             WRITE(0,'(A,I2,A,I3,A,Z8)')
     &        'F_MRCHUE_CTAU IS=',IS,' IBL=',IBL,
     &        ' CTAU=',TRANSFER(CTI,1)
            ENDIF
C---- GDB: trace MRCHUE Newton direct mode at station 51
            IF(IS.EQ.2 .AND. IBL.EQ.51) THEN
             WRITE(0,'(A,I3,A,Z8,A,Z8)')
     &        'F_MRCHUE_S51 iter=',ITBL,
     &        ' dsi=',TRANSFER(DSI,1),
     &        ' thi=',TRANSFER(THI,1)
            ENDIF
           ELSE
C---------- set prescribed Hk for inverse calculation at the current station
            IF(IBL.LT.ITRAN(IS)) THEN
C----------- laminar case: relatively slow increase in Hk downstream
             HTARG = HK1 + 0.03*(X2-X1)/T1
            ELSE IF(IBL.EQ.ITRAN(IS)) THEN
C----------- transition interval: weighted laminar and turbulent case
             HTARG = HK1 + (0.03*(XT-X1) - 0.15*(X2-XT))/T1
            ELSE IF(WAKE) THEN
C----------- turbulent wake case:
C-           asymptotic wake behavior with approximate Backward Euler
             CONST = 0.03*(X2-X1)/T1
             HK2 = HK1
             HK2 = HK2 - (HK2 +     CONST*(HK2-1.0)**3 - HK1)
     &                  /(1.0 + 3.0*CONST*(HK2-1.0)**2)
             HK2 = HK2 - (HK2 +     CONST*(HK2-1.0)**3 - HK1)
     &                  /(1.0 + 3.0*CONST*(HK2-1.0)**2)
             HK2 = HK2 - (HK2 +     CONST*(HK2-1.0)**3 - HK1)
     &                  /(1.0 + 3.0*CONST*(HK2-1.0)**2)
             HTARG = HK2
            ELSE
C----------- turbulent case: relatively fast decrease in Hk downstream
             HTARG = HK1 - 0.15*(X2-X1)/T1
            ENDIF
C
C---------- limit specified Hk to something reasonable
            HTRAW = HTARG
            IF(WAKE) THEN
             HTARG = MAX( HTARG , 1.01 )
            ELSE
             HTARG = MAX( HTARG , HMAX )
            ENDIF
            CALL TRACE_SEED_INVERSE_TARGET('MRCHUE', IS, IBL, ITBL,
     &           HK1, X1, X2, T1, XT, HKTEST, HMAX, HTRAW, HTARG)
C
            IF(IS.EQ.2 .AND. IBL.EQ.4) THEN
             WRITE(*,'(A,I2,1X,Z8,1X,Z8,1X,Z8,1X,Z8)')
     &        'F_HTARG24',ITBL,
     &        TRANSFER(HTARG,1),TRANSFER(HK1,1),
     &        TRANSFER(HTRAW,1),TRANSFER(T1,1)
            ENDIF
            WRITE(*,1300) IBL, HTARG
 1300       FORMAT(' MRCHUE: Inverse mode at', I4, '     Hk =', F8.3)
C
C---------- try again with prescribed Hk
            GO TO 100
C
           ENDIF
C
          ELSE
C
C-------- inverse mode (force Hk to prescribed value HTARG)
           VS2(4,1) = 0.
           VS2(4,2) = HK2_T2
           VS2(4,3) = HK2_D2
           VS2(4,4) = HK2_U2
           VSREZ(4) = HTARG - HK2
           IF(IS.EQ.2 .AND. IBL.EQ.83 .AND. ITBL.LE.2) THEN
             WRITE(0,'(A,I2,A,Z8.8,A,Z8.8,A,Z8.8)')
     &        'F_INV83_HT it=',ITBL,
     &        ' HTARG=',TRANSFER(HTARG,1),
     &        ' HK2=',TRANSFER(HK2,1),
     &        ' R4=',TRANSFER(VSREZ(4),1)
           ENDIF
           IF(IS.EQ.2 .AND. IBL.EQ.4 .AND.
     &        (ITBL.EQ.8.OR.ITBL.EQ.9)) THEN
            DO 9424 JJR=1, 4
              WRITE(*,'(A,I2,A,I1,A,Z8,1X,Z8,1X,Z8,1X,Z8,A,Z8)')
     &         'F_SYS24 it=',ITBL,' r',JJR-1,':',
     &         TRANSFER(VS2(JJR,1),1),
     &         TRANSFER(VS2(JJR,2),1),
     &         TRANSFER(VS2(JJR,3),1),
     &         TRANSFER(VS2(JJR,4),1),
     &         ' |',TRANSFER(VSREZ(JJR),1)
 9424       CONTINUE
           ENDIF
           IF(IS.EQ.1 .AND. IBL.EQ.58 .AND. ITBL.LE.25) THEN
             WRITE(*,'(A,I2,5(1X,Z8.8))')
     &        'F_INV58',ITBL,
     &        TRANSFER(HK2_T2,1),TRANSFER(HK2_D2,1),
     &        TRANSFER(HK2_U2,1),TRANSFER(HTARG,1),
     &        TRANSFER(VSREZ(4),1)
           ENDIF
           IF(IS.EQ.2 .AND. IBL.EQ.58) THEN
            DO 9581 JJR=1, 4
              WRITE(0,'(A,I2,A,I1,A,Z8,1X,Z8,1X,Z8,1X,Z8,A,Z8)')
     &         'F_MRCHUE58_SYS it=',ITBL,' r',JJR-1,':',
     &         TRANSFER(VS2(JJR,1),1),
     &         TRANSFER(VS2(JJR,2),1),
     &         TRANSFER(VS2(JJR,3),1),
     &         TRANSFER(VS2(JJR,4),1),
     &         ' |',TRANSFER(VSREZ(JJR),1)
 9581       CONTINUE
           ENDIF
           IF(IS.EQ.1 .AND. IBL.EQ.58 .AND. ITBL.EQ.4) THEN
             DO 9491 JJR=1, 4
               WRITE(*,'(A,I1,A,Z8,1X,Z8,1X,Z8,1X,Z8,A,Z8)')
     &          'F_INV58_4 r',JJR-1,':',
     &          TRANSFER(VS2(JJR,1),1),
     &          TRANSFER(VS2(JJR,2),1),
     &          TRANSFER(VS2(JJR,3),1),
     &          TRANSFER(VS2(JJR,4),1),
     &          ' |',TRANSFER(VSREZ(JJR),1)
 9491        CONTINUE
           ENDIF
C---- GDB: dump FULL 4x4 system at station 30 side 1 iter 6
           IF(IS.EQ.1 .AND. IBL.EQ.97 .AND. ITBL.EQ.1) THEN
             DO 9291 JJR=1, 4
               WRITE(0,'(A,I1,A,Z8,1X,Z8,1X,Z8,1X,Z8,A,Z8)')
     &          'F_SYS30 r',JJR-1,':',
     &          TRANSFER(VS2(JJR,1),1),
     &          TRANSFER(VS2(JJR,2),1),
     &          TRANSFER(VS2(JJR,3),1),
     &          TRANSFER(VS2(JJR,4),1),
     &          ' |',TRANSFER(VSREZ(JJR),1)
 9291        CONTINUE
           ENDIF
C
           IF(IS.EQ.1 .AND. IBL.EQ.58 .AND. ITBL.EQ.3) THEN
             DO 9399 JJR=1, 4
               WRITE(*,'(A,I1,A,Z8,1X,Z8,1X,Z8,1X,Z8,A,Z8)')
     &          'F_GSYS58 r',JJR-1,':',
     &          TRANSFER(VS2(JJR,1),1),
     &          TRANSFER(VS2(JJR,2),1),
     &          TRANSFER(VS2(JJR,3),1),
     &          TRANSFER(VS2(JJR,4),1),
     &          ' |',TRANSFER(VSREZ(JJR),1)
 9399        CONTINUE
           ENDIF
           CALL GAUSS(4,4,VS2,VSREZ,1)
           IF(IS.EQ.2 .AND. IBL.EQ.4 .AND.
     &        (ITBL.EQ.8.OR.ITBL.EQ.9)) THEN
            WRITE(*,'(A,I2,1X,Z8,1X,Z8,1X,Z8,1X,Z8)')
     &       'F_DELTA24',ITBL,
     &       TRANSFER(VSREZ(1),1),TRANSFER(VSREZ(2),1),
     &       TRANSFER(VSREZ(3),1),TRANSFER(VSREZ(4),1)
           ENDIF
           IF(IS.EQ.1 .AND. IBL.EQ.58 .AND. ITBL.LE.25) THEN
             WRITE(*,'(A,I2,4(1X,Z8.8),A,2(1X,Z8.8))')
     &        'F_DINV58',ITBL,
     &        TRANSFER(VSREZ(1),1),TRANSFER(VSREZ(2),1),
     &        TRANSFER(VSREZ(3),1),TRANSFER(VSREZ(4),1),
     &        ' T=',TRANSFER(THI,1),TRANSFER(DSI,1)
           ENDIF
C---- GDB: dump inverse delta at station 30 side 1
           IF(IS.EQ.1 .AND. IBL.EQ.97) THEN
             WRITE(0,'(A,I2,A,Z8,A,Z8,A,Z8,A,Z8,A,Z8,A,Z8)')
     &        'F_DELTA30 it=',ITBL,
     &        ' d0=',TRANSFER(VSREZ(1),1),
     &        ' d1=',TRANSFER(VSREZ(2),1),
     &        ' d2=',TRANSFER(VSREZ(3),1),
     &        ' d3=',TRANSFER(VSREZ(4),1),
     &        ' T=',TRANSFER(THI,1),
     &        ' D=',TRANSFER(DSI,1)
           ENDIF
C
C--------- added Ue clamp   MD  3 Apr 03
           DMAX = MAX( ABS(VSREZ(2)/THI),
     &                 ABS(VSREZ(3)/DSI),
     &                 ABS(VSREZ(4)/UEI)  )
           IF(IBL.GE.ITRAN(IS)) DMAX = MAX( DMAX , ABS(VSREZ(1)/CTI))
C
           RLX = 1.0
           IF(DMAX.GT.0.3) RLX = 0.3/DMAX
C---- GDB: trace MRCHUE wake station 85 iterate packet
           IF(IS.EQ.2 .AND. IBL.EQ.85) THEN
            WRITE(0,'(A,I2,A,L1,A,Z8,A,Z8,A,Z8,A,Z8,A,Z8,A,Z8)')
     &       'F_MUE85_PRE it=',ITBL,' D=',DIRECT,
     &       ' T0=',TRANSFER(THI,1),
     &       ' D0=',TRANSFER(DSI,1),
     &       ' U0=',TRANSFER(UEI,1),
     &       ' C0=',TRANSFER(CTI,1),
     &       ' RL=',TRANSFER(RLX,1),
     &       ' DM=',TRANSFER(DMAX,1)
            WRITE(0,'(A,I2,A,Z8,A,Z8,A,Z8,A,Z8,A,Z8,A,Z8,A,Z8,A,Z8)')
     &       'F_MUE85_DEL it=',ITBL,
     &       ' R0=',TRANSFER(VSREZ(1),1),
     &       ' R1=',TRANSFER(VSREZ(2),1),
     &       ' R2=',TRANSFER(VSREZ(3),1),
     &       ' R3=',TRANSFER(VSREZ(4),1),
     &       ' d0=',TRANSFER(VSREZ(1),1),
     &       ' d1=',TRANSFER(VSREZ(2),1),
     &       ' d2=',TRANSFER(VSREZ(3),1),
     &       ' d3=',TRANSFER(VSREZ(4),1)
           ENDIF
C
           IF(IS.EQ.1 .AND. IBL.EQ.58) THEN
             WRITE(*,'(A,I2,A,Z8,A,Z8,A,Z8,A,Z8)')
     &        'F_D58 it=',ITBL,
     &        ' d0=',TRANSFER(VSREZ(1),1),
     &        ' d1=',TRANSFER(VSREZ(2),1),
     &        ' d2=',TRANSFER(VSREZ(3),1),
     &        ' rlx=',TRANSFER(RLX,1)
           ENDIF
           IF(IS.EQ.2 .AND. IBL.EQ.4 .AND.
     &        (ITBL.EQ.8.OR.ITBL.EQ.9)) THEN
            WRITE(*,'(A,I2,1X,Z8,1X,Z8,1X,Z8,1X,Z8)')
     &       'F_UPD24_PRE',ITBL,
     &       TRANSFER(THI,1),TRANSFER(DSI,1),
     &       TRANSFER(RLX,1),TRANSFER(DMAX,1)
           ENDIF
C--------- update variables
           IF(IS.EQ.2 .AND. IBL.EQ.58) THEN
              WRITE(0,'(A,I2,A,L1,A,L1,A,Z8,A,Z8,A,Z8,A,Z8,A,Z8,
     &          A,Z8,A,Z8,A,Z8)')
     &         'F_MRCHUE58 it=',ITBL,' D=',DIRECT,' T=',TRAN,
     &         ' D_pre=',TRANSFER(DSI,1),
     &         ' T_pre=',TRANSFER(THI,1),
     &         ' d2=',TRANSFER(VSREZ(3),1),
     &         ' d1=',TRANSFER(VSREZ(2),1),
     &         ' rlx=',TRANSFER(RLX,1),
     &         ' R0=',TRANSFER(VSREZ(1),1),
     &         ' R1=',TRANSFER(VSREZ(2),1),
     &         ' R2=',TRANSFER(VSREZ(3),1)
           ENDIF
ccc           IF(IBL.LT.ITRAN(IS)) AMI = AMI + RLX*VSREZ(1)
           IF(IBL.GE.ITRAN(IS)) CTI = CTI + RLX*VSREZ(1)
           IF(IS.EQ.2 .AND. IBL.EQ.83) THEN
            WRITE(0,'(A,Z8,A,Z8,A,Z8,A,Z8,A,Z8,A,Z8)')
     &       'F_MUE83_UPD T0=',TRANSFER(THI,1),
     &       ' D0=',TRANSFER(DSI,1),
     &       ' rlx=',TRANSFER(RLX,1),
     &       ' r1=',TRANSFER(VSREZ(2),1),
     &       ' r2=',TRANSFER(VSREZ(3),1),
     &       ' r3=',TRANSFER(VSREZ(4),1)
           ENDIF
           THI = THI + RLX*VSREZ(2)
           DSI = DSI + RLX*VSREZ(3)
           UEI = UEI + RLX*VSREZ(4)
           IF(IS.EQ.2 .AND. IBL.EQ.4 .AND.
     &        (ITBL.EQ.8.OR.ITBL.EQ.9)) THEN
            WRITE(*,'(A,I2,1X,Z8,1X,Z8,1X,Z8)')
     &       'F_UPD24_POST',ITBL,
     &       TRANSFER(THI,1),TRANSFER(DSI,1),
     &       TRANSFER(UEI,1)
           ENDIF
           IF(IS.EQ.2 .AND. IBL.EQ.58) THEN
              WRITE(0,'(A,I2,A,Z8,A,Z8)')
     &         'F_MRCHUE58 it=',ITBL,
     &         ' D_add=',TRANSFER(DSI,1),
     &         ' T_add=',TRANSFER(THI,1)
           ENDIF
C---- GDB: trace MRCHUE Newton at station 51
           IF(IS.EQ.2 .AND. IBL.EQ.51) THEN
            WRITE(0,'(A,I3,A,Z8,A,Z8)')
     &       'F_MRCHUE_S51 iter=',ITBL,
     &       ' dsi=',TRANSFER(DSI,1),
     &       ' thi=',TRANSFER(THI,1)
           ENDIF
C
          ENDIF
C
C-------- eliminate absurd transients
          IF(IBL.GE.ITRAN(IS)) THEN
           CTI = MIN(CTI , 0.30 )
           CTI = MAX(CTI , 0.0000001 )
          ENDIF
C
          IF(IBL.LE.IBLTE(IS)) THEN
            HKLIM = 1.02
          ELSE
            HKLIM = 1.00005
          ENDIF
          MSQ = UEI*UEI*HSTINV / (GM1BL*(1.0 - 0.5*UEI*UEI*HSTINV))
          DSW = DSI - DSWAKI
          IF(IS.EQ.2 .AND. IBL.EQ.83) THEN
           WRITE(0,'(A,Z8,A,Z8,A,Z8)')
     &      'F_WK83 DSI=',TRANSFER(DSI,1),
     &      ' THI=',TRANSFER(THI,1),
     &      ' DSW=',TRANSFER(DSW,1)
          ENDIF
          CALL DSLIM(DSW,THI,UEI,MSQ,HKLIM)
          DSI = DSW + DSWAKI
          IF(IS.EQ.2 .AND. IBL.EQ.85) THEN
           WRITE(0,'(A,I2,A,Z8,A,Z8,A,Z8,A,Z8,A,Z8,A,Z8)')
     &      'F_MUE85_POST it=',ITBL,
     &      ' T=',TRANSFER(THI,1),
     &      ' D=',TRANSFER(DSI,1),
     &      ' U=',TRANSFER(UEI,1),
     &      ' C=',TRANSFER(CTI,1),
     &      ' DSW=',TRANSFER(DSW,1),
     &      ' MSQ=',TRANSFER(MSQ,1)
          ENDIF
          IF(IS.EQ.2 .AND. IBL.GE.85 .AND. IBL.LE.90
     &         .AND. ITBL.LE.6) THEN
            WRITE(0,'(A,I3,A,I2,A,Z8,A,Z8,A,Z8,A,Z8,A,Z8,A,Z8)')
     &       'F_WK8790_POST I=',IBL,' IT=',ITBL,
     &       ' T=',TRANSFER(THI,1),
     &       ' D=',TRANSFER(DSI,1),
     &       ' U=',TRANSFER(UEI,1),
     &       ' C=',TRANSFER(CTI,1),
     &       ' DSW=',TRANSFER(DSW,1),
     &       ' MSQ=',TRANSFER(MSQ,1)
          ENDIF
C---- GDB: dump after DSLIM at station 3 side 1
          IF(IS.EQ.1 .AND. IBL.EQ.3) THEN
            WRITE(0,'(A,I2,A,Z8,A,Z8)')
     &       'F_DSLIM3 it=',ITBL,
     &       ' T=',TRANSFER(THI,1),
     &       ' D=',TRANSFER(DSI,1)
          ENDIF
          IF(IS.EQ.2 .AND. IBL.EQ.58) THEN
            WRITE(0,'(A,I2,A,Z8,A,Z8)')
     &       'F_MRCHUE58 it=',ITBL,
     &       ' D_lim=',TRANSFER(DSI,1),
     &       ' T_lim=',TRANSFER(THI,1)
          ENDIF
C---- GDB: dump after update+DSLIM at station 97 side 1
          IF(IS.EQ.1 .AND. IBL.EQ.97
     &       .AND. (ITBL.EQ.1.OR.ITBL.EQ.25)) THEN
            WRITE(0,'(A,I2,A,Z8,A,Z8)')
     &       'F_UPD97 it=',ITBL,
     &       ' T=',TRANSFER(THI,1),
     &       ' D=',TRANSFER(DSI,1)
          ENDIF
C---- stf86361 trace: side 1 ibl 90-91 Newton iter
          IF(IS.EQ.1 .AND. IBL.GE.90 .AND. IBL.LE.91) THEN
            WRITE(0,'(A,I3,A,I2,A,Z8,A,Z8,A,Z8,A,Z8,A,Z8)')
     &       'F_STF91 I=',IBL,' IT=',ITBL,
     &       ' T=',TRANSFER(THI,1),
     &       ' D=',TRANSFER(DSI,1),
     &       ' U=',TRANSFER(UEI,1),
     &       ' C=',TRANSFER(CTI,1),
     &       ' DM=',TRANSFER(DMAX,1)
          ENDIF
C---- stf86361 low-Re COM1 trace at ibl=81 entry
          IF(IS.EQ.2 .AND. IBL.EQ.81 .AND. ITBL.EQ.1) THEN
            WRITE(0,'(A,9(A,Z8))')
     &       'F_STFLO_COM1',
     &       ' HK1=',TRANSFER(HK1,1),
     &       ' RT1=',TRANSFER(RT1,1),
     &       ' M21=',TRANSFER(M1,1),
     &       ' CF1=',TRANSFER(CF1,1),
     &       ' DI1=',TRANSFER(DI1,1),
     &       ' HS1=',TRANSFER(HS1,1),
     &       ' US1=',TRANSFER(US1,1),
     &       ' CQ1=',TRANSFER(CQ1,1),
     &       ' DE1=',TRANSFER(DE1,1)
            WRITE(0,'(A,2(A,Z8))')
     &       'F_STFLO_TD1',
     &       ' T1=',TRANSFER(T1,1),
     &       ' D1=',TRANSFER(D1,1)
          ENDIF
C---- stf86361 low-Re trace: side 2 ibl 80-82 Newton iter
          IF(IS.EQ.2 .AND. IBL.GE.80 .AND. IBL.LE.82) THEN
            WRITE(0,'(A,I3,A,I2,A,Z8,A,Z8,A,Z8,A,Z8,A,Z8,A,Z8,A,I3,A,Z8)')
     &       'F_STFLO I=',IBL,' IT=',ITBL,
     &       ' T=',TRANSFER(THI,1),
     &       ' D=',TRANSFER(DSI,1),
     &       ' U=',TRANSFER(UEI,1),
     &       ' C=',TRANSFER(CTI,1),
     &       ' DM=',TRANSFER(DMAX,1),
     &       ' Xs=',TRANSFER(XSSI(IBL,IS),1),
     &       ' iw=',IW,
     &       ' WG=',TRANSFER(WGAP(IW),1)
          ENDIF
C
C---- NACA 1408 debug: real DMAX at station 71 after update
          IF(IS.EQ.2 .AND. IBL.EQ.71) THEN
            WRITE(0,'(A,I2,A,Z8,A,Z8,A,Z8,A,Z8,A,Z8,A,Z8)')
     &       'F_MUE71_FINAL it=',ITBL,
     &       ' DM=',TRANSFER(DMAX,1),
     &       ' T=',TRANSFER(THI,1),
     &       ' D=',TRANSFER(DSI,1),
     &       ' U=',TRANSFER(UEI,1),
     &       ' CTI=',TRANSFER(CTI,1),
     &       ' AMI=',TRANSFER(AMI,1)
          ENDIF
          IF(DMAX.LE.1.0E-5) GO TO 110
C
  100   CONTINUE
        WRITE(*,1350) IBL, IS, DMAX
 1350   FORMAT(' MRCHUE: Convergence failed at',I4,'  side',I2,
     &         '    Res =', E12.4)
C
C------ the current unconverged solution might still be reasonable...
CCC        IF(DMAX .LE. 0.1) GO TO 110
        IF(DMAX .LE. 0.1) GO TO 109
C
C------- the current solution is garbage --> extrapolate values instead
         IF(IBL.GT.3) THEN
          IF(IBL.LE.IBLTE(IS)) THEN
           THI = THET(IBM,IS) * (XSSI(IBL,IS)/XSSI(IBM,IS))**0.5
           DSI = DSTR(IBM,IS) * (XSSI(IBL,IS)/XSSI(IBM,IS))**0.5
C-------- stf86361 trace: capture extrapolation math bit-exact
           IF(IS.EQ.1 .AND. IBL.EQ.91) THEN
             WRITE(0,'(A,Z8,A,Z8,A,Z8,A,Z8,A,Z8)')
     &        'F_STF91_EXTRAP Xibl=',TRANSFER(XSSI(IBL,IS),1),
     &        ' Xibm=',TRANSFER(XSSI(IBM,IS),1),
     &        ' Tibm=',TRANSFER(THET(IBM,IS),1),
     &        ' Thi=',TRANSFER(THI,1),
     &        ' Dsi=',TRANSFER(DSI,1)
           ENDIF
          ELSE IF(IBL.EQ.IBLTE(IS)+1) THEN
           CTI = CTE
           THI = TTE
           DSI = DTE
          ELSE
           THI = THET(IBM,IS)
           RATLEN = (XSSI(IBL,IS)-XSSI(IBM,IS)) / (10.0*DSTR(IBM,IS))
           DSI = (DSTR(IBM,IS) + THI*RATLEN) / (1.0 + RATLEN)
          ENDIF
          IF(IBL.EQ.ITRAN(IS)) CTI = 0.05
          IF(IBL.GT.ITRAN(IS)) CTI = CTAU(IBM,IS)
C
          UEI = UEDG(IBL,IS)
          IF(IBL.GT.2 .AND. IBL.LT.NBL(IS))
     &     UEI = 0.5*(UEDG(IBL-1,IS) + UEDG(IBL+1,IS))
         ENDIF
C
 109     CALL BLPRV(XSI,AMI,CTI,THI,DSI,DSWAKI,UEI)
         CALL BLKIN
C
C------- check for transition and set appropriate flags and things
         IF((.NOT.SIMI) .AND. (.NOT.TURB)) THEN
          CALL TRCHEK
          AMI = AMPL2
          IF(     TRAN) ITRAN(IS) = IBL
          IF(.NOT.TRAN) ITRAN(IS) = IBL+2
         ENDIF
C
C------- set all other extrapolated values for current station
         IF(IBL.LT.ITRAN(IS)) CALL BLVAR(1)
         IF(IBL.GE.ITRAN(IS)) CALL BLVAR(2)
         IF(WAKE) CALL BLVAR(3)
         IF(IS.EQ.1 .AND. IBL.LE.4) THEN
           WRITE(0,'(A,I1,A,I2,6(A,Z8))') 'F_MUE_BLV s=',IS,' ibl=',IBL,
     &      ' HK=',TRANSFER(HK2,1),' RT=',TRANSFER(RT2,1),
     &      ' HS=',TRANSFER(HS2,1),' US=',TRANSFER(US2,1),
     &      ' CF=',TRANSFER(CF2,1),' DI=',TRANSFER(DI2,1)
         ENDIF
C---- stf86361 trace: BLVAR output at label 109 for IS=2 IBL=80
         IF(IS.EQ.2 .AND. IBL.EQ.80) THEN
           WRITE(0,'(A,8(A,Z8))') 'F_STFLO_109_BLVAR',
     &      ' HK=',TRANSFER(HK2,1),
     &      ' RT=',TRANSFER(RT2,1),
     &      ' M2=',TRANSFER(M2,1),
     &      ' H2=',TRANSFER(H2,1),
     &      ' HS=',TRANSFER(HS2,1),
     &      ' US=',TRANSFER(US2,1),
     &      ' DI=',TRANSFER(DI2,1),
     &      ' CQ=',TRANSFER(CQ2,1)
         ENDIF
C---- s9104 trace: BLVAR output at label 109 for IS=2 IBL=100
         IF(IS.EQ.2 .AND. IBL.EQ.100) THEN
           WRITE(0,'(A,8(A,Z8))') 'F_S9104_109_BLVAR',
     &      ' HK=',TRANSFER(HK2,1),
     &      ' RT=',TRANSFER(RT2,1),
     &      ' M2=',TRANSFER(M2,1),
     &      ' H2=',TRANSFER(H2,1),
     &      ' HS=',TRANSFER(HS2,1),
     &      ' US=',TRANSFER(US2,1),
     &      ' DI=',TRANSFER(DI2,1),
     &      ' CQ=',TRANSFER(CQ2,1)
         ENDIF
C
         IF(IBL.LT.ITRAN(IS)) CALL BLMID(1)
         IF(IBL.GE.ITRAN(IS)) CALL BLMID(2)
         IF(WAKE) CALL BLMID(3)
C
C------ pick up here after the Newton iterations
  110   CONTINUE
C
C------ store primary variables
        IF(IBL.LT.ITRAN(IS)) CTAU(IBL,IS) = AMI
        IF(IBL.GE.ITRAN(IS)) CTAU(IBL,IS) = CTI
        THET(IBL,IS) = THI
        DSTR(IBL,IS) = DSI
        UEDG(IBL,IS) = UEI
        MASS(IBL,IS) = DSI*UEI
        IF(IS.EQ.2 .AND. (IBL.GE.56 .AND. IBL.LE.60)) THEN
         WRITE(0,'(A,I3,A,Z8,A,Z8,A,Z8)')
     &    'F_MRCHUE_ACPT58 IBL=',IBL,
     &    ' D=',TRANSFER(DSI,1),
     &    ' T=',TRANSFER(THI,1),
     &    ' U=',TRANSFER(UEI,1)
        ENDIF
        IF(IS.EQ.2 .AND. IBL.EQ.81) THEN
          WRITE(0,'(A,Z8,A,Z8,A,Z8)')
     &     'F_MRCHUE_WK81 DSTR=',TRANSFER(DSI,1),
     &     ' UEDG=',TRANSFER(UEI,1),
     &     ' MASS=',TRANSFER(MASS(IBL,IS),1)
        ENDIF
        IF(IS.EQ.2 .AND. IBL.GE.87 .AND. IBL.LE.90) THEN
         WRITE(0,'(A,I3,A,Z8,A,Z8,A,Z8,A,Z8,A,Z8)')
     &    'F_WK8790_ACPT I=',IBL,
     &    ' T=',TRANSFER(THI,1),
     &    ' D=',TRANSFER(DSI,1),
     &    ' U=',TRANSFER(UEI,1),
     &    ' C=',TRANSFER(CTAU(IBL,IS),1),
     &    ' M=',TRANSFER(MASS(IBL,IS),1)
        ENDIF
C---- GDB: dump BL vars at stations 28-32 side 1
        IF(IS.EQ.1 .AND. IBL.GE.28 .AND. IBL.LE.32) THEN
          WRITE(0,'(A,I3,A,Z8,A,Z8,A,Z8,A,Z8,A,Z8,A,Z8)')
     &     'F_BL I=',IBL,
     &     ' T=',TRANSFER(THI,1),' D=',TRANSFER(DSI,1),
     &     ' HK=',TRANSFER(HK2,1),' HS=',TRANSFER(HS2,1),
     &     ' CF=',TRANSFER(CF2,1),' DI=',TRANSFER(DI2,1)
        ENDIF
        IF(IBL.LT.ITRAN(IS)) THEN
         CALL TRACE_LAMINAR_SEED_FINAL('MRCHUE', IS, IBL, THI, DSI,
     &        AMI, MASS(IBL,IS))
        ENDIF
        TAU(IBL,IS)  = 0.5*R2*U2*U2*CF2
        DIS(IBL,IS)  =     R2*U2*U2*U2*DI2*HS2*0.5
        CTQ(IBL,IS)  = CQ2
        DELT(IBL,IS) = DE2
        TSTR(IBL,IS) = HS2*T2
C
C------ set "1" variables to "2" variables for next streamwise station
        CALL BLPRV(XSI,AMI,CTI,THI,DSI,DSWAKI,UEI)
        CALL BLKIN
        DO 310 ICOM=1, NCOM
          COM1(ICOM) = COM2(ICOM)
  310   CONTINUE
C
C------ turbulent intervals will follow transition interval or TE
        IF(TRAN .OR. IBL.EQ.IBLTE(IS)) THEN
         TURB = .TRUE.
C
C------- save transition location
         TFORCE(IS) = TRFORC
         XSSITR(IS) = XT
        ENDIF
C
        TRAN = .FALSE.
C
        IF(IBL.EQ.IBLTE(IS)) THEN
         THI = THET(IBLTE(1),1) + THET(IBLTE(2),2)
         DSI = DSTR(IBLTE(1),1) + DSTR(IBLTE(2),2) + ANTE
         IF(IS.EQ.2) THEN
          WRITE(0,'(A,Z8,A,Z8,A,Z8,A,Z8)')
     &     'F_TEMERGE DSI=',TRANSFER(DSI,1),
     &     ' UpD=',TRANSFER(DSTR(IBLTE(1),1),1),
     &     ' LoD=',TRANSFER(DSTR(IBLTE(2),2),1),
     &     ' ANTE=',TRANSFER(ANTE,1)
         ENDIF
        ENDIF
C
 1000 CONTINUE
 2000 CONTINUE
C
C---- GDB: dump final MRCHUE state for all stations
      DO 2111 JIS=1, 2
        DO 2112 JIBL=3, NBL(JIS)
          WRITE(0,'(A,I2,A,I4,A,Z8,A,Z8,A,Z8)')
     &     'F_POST IS=',JIS,' I=',JIBL,
     &     ' T=',TRANSFER(THET(JIBL,JIS),1),
     &     ' D=',TRANSFER(DSTR(JIBL,JIS),1),
     &     ' S=',TRANSFER(CTAU(JIBL,JIS),1)
 2112   CONTINUE
 2111 CONTINUE
C
      RETURN
      END


      SUBROUTINE MRCHDU
C----------------------------------------------------
C     Marches the BLs and wake in mixed mode using
C     the current Ue and Hk.
C----------------------------------------------------
      INCLUDE 'XFOIL.INC'
      INCLUDE 'XBL.INC'
      REAL VTMP(4,5), VZTMP(4)
      REAL MSQ
ccc   REAL MDI
      INTEGER MRCHDU_COUNT
      SAVE MRCHDU_COUNT
      DATA MRCHDU_COUNT /0/
C
      DATA DEPS / 5.0E-6 /
      MRCHDU_COUNT = MRCHDU_COUNT + 1
C
C---- constant controlling how far Hk is allowed to deviate
C-    from the specified value.
      SENSWT = 1000.0
C
      DO 2000 IS=1, 2
C
C---- set forced transition arc length position
      CALL XIFSET(IS)
C
C---- set leading edge pressure gradient parameter  x/u du/dx
      IBL = 2
      XSI = XSSI(IBL,IS)
      UEI = UEDG(IBL,IS)
CCC      BULE = LOG(UEDG(IBL+1,IS)/UEI) / LOG(XSSI(IBL+1,IS)/XSI)
CCC      BULE = MAX( -.08 , BULE )
      BULE = 1.0
C
C---- old transition station
      ITROLD = ITRAN(IS)
C
      TRAN = .FALSE.
      TURB = .FALSE.
      ITRAN(IS) = IBLTE(IS)
C
C---- march downstream
      DO 1000 IBL=2, NBL(IS)
        IBM = IBL-1
C
        SIMI = IBL.EQ.2
        WAKE = IBL.GT.IBLTE(IS)
C
C------ initialize current station to existing variables
        XSI = XSSI(IBL,IS)
        UEI = UEDG(IBL,IS)
        THI = THET(IBL,IS)
        DSI = DSTR(IBL,IS)
C---- n6h20 trace: COM1 (= ibm secondary) at start of i=72 across calls 7-11
        IF(MRCHDU_COUNT.GE.7 .AND. MRCHDU_COUNT.LE.11 .AND. IS.EQ.2 .AND. IBL.EQ.72) THEN
          WRITE(0,'(A,I2,A,I3,A,I3,11(A,Z8))')
     &     'F_MDU',MRCHDU_COUNT,'_COM1 ibl=',IBL,' ibm=',IBM,
     &     ' T1=',TRANSFER(THET(IBM,IS),1),
     &     ' D1=',TRANSFER(DSTR(IBM,IS),1),
     &     ' U1=',TRANSFER(UEDG(IBM,IS),1),
     &     ' HK1=',TRANSFER(HK1,1),
     &     ' RT1=',TRANSFER(RT1,1),
     &     ' CF1=',TRANSFER(CF1,1),
     &     ' HS1=',TRANSFER(HS1,1),
     &     ' DI1=',TRANSFER(DI1,1),
     &     ' US1=',TRANSFER(US1,1),
     &     ' CQ1=',TRANSFER(CQ1,1),
     &     ' DE1=',TRANSFER(DE1,1)
        ENDIF
        IF(IS.EQ.1 .AND. IBL.GE.3 .AND. IBL.LE.11
     &     .AND. MRCHDU_COUNT.GE.18 .AND. MRCHDU_COUNT.LE.21) THEN
         WRITE(0,'(A,I3,A,I3,A,Z8,A,Z8,A,Z8,A,Z8,A,Z8)')
     &    'F_PFCM_IN mc=',MRCHDU_COUNT,' ibl=',IBL,
     &    ' X=',TRANSFER(XSI,1),
     &    ' T=',TRANSFER(THI,1),
     &    ' D=',TRANSFER(DSI,1),
     &    ' U=',TRANSFER(UEI,1),
     &    ' C=',TRANSFER(CTAU(IBL,IS),1)
        ENDIF
        IF(IS.EQ.1 .AND. IBL.EQ.27 .AND. TRACE_OUTER.EQ.8) THEN
         WRITE(0,'(A,Z8,A,Z8,A,Z8,A,Z8,A,I4,A,L1,A,L1)')
     &    'F_SEED27 THI=',TRANSFER(THI,1),
     &    ' DSI=',TRANSFER(DSI,1),
     &    ' CTI=',TRANSFER(CTI,1),
     &    ' CTAU=',TRANSFER(CTAU(IBL,IS),1),
     &    ' ITROLD=',ITROLD,
     &    ' TURB=',TURB,
     &    ' TRAN=',TRAN
        ENDIF
        IF(IS.EQ.2 .AND. IBL.EQ.85) THEN
         WRITE(0,'(A,Z8,A,Z8,A,Z8)')
     &    'F_INIT85 UEI=',TRANSFER(UEI,1),
     &    ' THI=',TRANSFER(THI,1),
     &    ' DSI=',TRANSFER(DSI,1)
        ENDIF
        IF(IS.EQ.2 .AND. (IBL.EQ.57 .OR. IBL.EQ.58)) THEN
         WRITE(0,'(A,I3,A,Z8,A,Z8,A,Z8)')
     &    'F_UPD58 INIT IBL=',IBL,
     &    ' D=',TRANSFER(DSI,1),
     &    ' T=',TRANSFER(THI,1),
     &    ' U=',TRANSFER(UEI,1)
        ENDIF
C---- GDB: MRCHDU seed dump for wake
        IF(WAKE) THEN
         WRITE(0,'(A,I2,A,I3,A,Z8,A,Z8,A,Z8)')
     &    'F_MRCHDU_SEED IS=',IS,' IBL=',IBL,
     &    ' DSI=',TRANSFER(DSI,1),
     &    ' DSWAKI=',TRANSFER(DSWAKI,1),
     &    ' DSI-DW=',TRANSFER(DSI-DSWAKI,1)
        ENDIF
CCC        MDI = MASS(IBL,IS)
C
C------ fixed BUG   MD 7 June 99
        IF(IBL.LT.ITROLD) THEN
         AMI = CTAU(IBL,IS)
         CTI = 0.03
        ELSE
         CTI = CTAU(IBL,IS)
         IF(CTI.LE.0.0) CTI = 0.03
        ENDIF
C
CCC        DSI = MDI/UEI
C
        IF(WAKE) THEN
         IW = IBL - IBLTE(IS)
         DSWAKI = WGAP(IW)
        ELSE
         DSWAKI = 0.
        ENDIF
C
        IF(IBL.LE.IBLTE(IS)) DSI = MAX(DSI-DSWAKI,1.02000*THI) + DSWAKI
        IF(IBL.GT.IBLTE(IS)) DSI = MAX(DSI-DSWAKI,1.00005*THI) + DSWAKI
        IF(IS.EQ.1 .AND. IBL.EQ.2) THEN
         WRITE(*,'(A,I2,1X,Z8,1X,Z8,1X,Z8)')
     &    'F_MDU_S2',MRCHDU_COUNT,
     &    TRANSFER(THI,1),TRANSFER(DSI,1),TRANSFER(UEI,1)
        ENDIF
        IF(IS.EQ.2 .AND. IBL.EQ.4 .AND. MRCHDU_COUNT.LE.10) THEN
         WRITE(*,'(A,Z8,1X,Z8,1X,Z8,1X,Z8)')
     &    'F_SEED24',
     &    TRANSFER(THI,1),TRANSFER(DSI,1),
     &    TRANSFER(UEI,1),TRANSFER(CTI,1)
        ENDIF
        IF(IS.EQ.2 .AND. (IBL.EQ.57 .OR. IBL.EQ.58)) THEN
         WRITE(0,'(A,I3,A,Z8,A,Z8)')
     &    'F_UPD58 CLMP IBL=',IBL,
     &    ' D=',TRANSFER(DSI,1),
     &    ' T=',TRANSFER(THI,1)
        ENDIF
        IF(IS.EQ.2 .AND. IBL.EQ.5 .AND. MRCHDU_COUNT.EQ.2) THEN
         WRITE(*,'(A,Z8,1X,Z8,1X,Z8,1X,L1,1X,L1)')
     &    'F_MDU_TR5',
     &    TRANSFER(THI,1),TRANSFER(DSI,1),
     &    TRANSFER(UEI,1),TRAN,TURB
        ENDIF
C
C------ Newton iteration loop for current station
        DO 100 ITBL=1, 25
          IF(IS.EQ.2 .AND. IBL.EQ.24 .AND. MRCHDU_COUNT.EQ.22) THEN
           WRITE(0,'(A,I2,5(A,Z8))')
     &      'F_MDU22_24 it=',ITBL,
     &      ' T=',TRANSFER(THI,1),' D=',TRANSFER(DSI,1),
     &      ' U=',TRANSFER(UEI,1),' C=',TRANSFER(CTI,1),
     &      ' A=',TRANSFER(AMI,1)
          ENDIF
C-------- fx63120 transition station trace (IS=2 IBL=61 first MRCHDU call)
          IF(IS.EQ.2 .AND. IBL.EQ.61 .AND. MRCHDU_COUNT.EQ.1) THEN
           WRITE(0,'(A,I2,5(A,Z8))')
     &      'F_FX61 it=',ITBL,
     &      ' T=',TRANSFER(THI,1),
     &      ' D=',TRANSFER(DSI,1),
     &      ' U=',TRANSFER(UEI,1),
     &      ' CTI=',TRANSFER(CTI,1),
     &      ' AMI=',TRANSFER(AMI,1)
          ENDIF
C-------- Trace MRCHDU iter 12 (MRCHDU_COUNT=12) at wake stns 66-68 side 2
          IF(IS.EQ.2 .AND. IBL.GE.65 .AND. IBL.LE.68
     &       .AND. MRCHDU_COUNT.EQ.12) THEN
           WRITE(0,'(A,I4,A,I2,A,Z8,A,Z8,A,Z8,A,Z8,A,Z8)')
     &      'F_MDU12_IBL',IBL,' it=',ITBL,
     &      ' T=',TRANSFER(THI,1),
     &      ' D=',TRANSFER(DSI,1),
     &      ' U=',TRANSFER(UEI,1),
     &      ' CTI=',TRANSFER(CTI,1),
     &      ' AMI=',TRANSFER(AMI,1)
          ENDIF
C-------- Trace MRCHDU iter 10 (n6h20 divergence) at IBL=66 side 2
          IF(IS.EQ.2 .AND. IBL.EQ.66 .AND. MRCHDU_COUNT.EQ.10) THEN
           WRITE(0,'(A,I2,A,Z8,A,Z8,A,Z8,A,Z8)')
     &      'F_MDU10_66 it=',ITBL,
     &      ' T=',TRANSFER(THI,1),
     &      ' D=',TRANSFER(DSI,1),
     &      ' U=',TRANSFER(UEI,1),
     &      ' CTI=',TRANSFER(CTI,1)
          ENDIF
C-------- BLVAR outputs at IBL=66 iter 10 (after BLVAR call)
          IF(IS.EQ.2 .AND. IBL.EQ.66 .AND. MRCHDU_COUNT.EQ.10
     &       .AND. ITBL.LE.3) THEN
           WRITE(0,'(A,I2,4(A,Z8))')
     &      'F_VSREZ10_66 it=',ITBL,
     &      ' R0=',TRANSFER(VSREZ(1),1),
     &      ' R1=',TRANSFER(VSREZ(2),1),
     &      ' R2=',TRANSFER(VSREZ(3),1),
     &      ' R3=',TRANSFER(VSREZ(4),1)
          ENDIF
          IF(IS.EQ.2 .AND. IBL.EQ.69 .AND. MRCHDU_COUNT.EQ.1) THEN
           WRITE(0,'(A,I2,6(A,Z8))')
     &      'F_MDU69 it=',ITBL,
     &      ' T=',TRANSFER(THI,1),
     &      ' D=',TRANSFER(DSI,1),
     &      ' U=',TRANSFER(UEI,1),
     &      ' T1=',TRANSFER(THET(IBM,IS),1),
     &      ' D1=',TRANSFER(DSTR(IBM,IS),1),
     &      ' CTI=',TRANSFER(CTI,1)
          ENDIF
          IF(IS.EQ.2 .AND. (IBL.EQ.93.OR.IBL.EQ.98)
     &       .AND. MRCHDU_COUNT.LE.3) THEN
           WRITE(0,'(A,I3,A,I1,A,I2,3(A,Z8))')
     &      'F_MDUI',IBL,
     &      ' mc=',MRCHDU_COUNT,' it=',ITBL,
     &      ' T=',TRANSFER(THI,1),
     &      ' D=',TRANSFER(DSI,1),
     &      ' U=',TRANSFER(UEI,1)
          ENDIF
C-------- Trace iter loop at IS=2 IBL=90..99 in MRCHDU_COUNT=3
          IF(IS.EQ.2 .AND. IBL.GE.90 .AND. IBL.LE.99
     &       .AND. MRCHDU_COUNT.EQ.3) THEN
           WRITE(0,'(A,I4,A,I2,5(A,Z8))')
     &      'F_MDU3 i=',IBL,' it=',ITBL,
     &      ' T=',TRANSFER(THI,1),
     &      ' D=',TRANSFER(DSI,1),
     &      ' U=',TRANSFER(UEI,1),
     &      ' T1=',TRANSFER(THET(IBM,IS),1),
     &      ' D1=',TRANSFER(DSTR(IBM,IS),1)
          ENDIF
C
C-------- assemble 10x3 linearized system for dCtau, dTh, dDs, dUe, dXi
C         at the previous "1" station and the current "2" station
C         (the "1" station coefficients will be ignored)
C
          CALL BLPRV(XSI,AMI,CTI,THI,DSI,DSWAKI,UEI)
          CALL BLKIN
C
          IF(IS.EQ.2 .AND. IBL.EQ.83 .AND. ITBL.EQ.1
     &       .AND. MRCHDU_COUNT.EQ.1) THEN
           WRITE(0,'(A,7(1X,Z8))')
     &      'F_MDU_COM1_83',
     &      TRANSFER(HK1,1),TRANSFER(RT1,1),
     &      TRANSFER(CF1,1),TRANSFER(DI1,1),
     &      TRANSFER(HS1,1),TRANSFER(US1,1),
     &      TRANSFER(CQ1,1)
           WRITE(0,'(A,6(1X,Z8))')
     &      'F_MDU_IN83',
     &      TRANSFER(T1,1),TRANSFER(D1,1),
     &      TRANSFER(U1,1),TRANSFER(T2,1),
     &      TRANSFER(D2,1),TRANSFER(U2,1)
          ENDIF
C-------- trace COM1+COM2 at similarity station (IBL=2) for MRCHDU calls 1-2
          IF(IBL.EQ.2 .AND. ITBL.LE.3 .AND. MRCHDU_COUNT.LE.2) THEN
           WRITE(0,'(A,I1,A,I2,A,I2,A,Z8,A,Z8,A,Z8,A,Z8,A,Z8,A,Z8)')
     &      'F_SIMI_COM1 S=',IS,' C=',MRCHDU_COUNT,' it=',ITBL,
     &      ' HK1=',TRANSFER(HK1,1),
     &      ' HS1=',TRANSFER(HS1,1),
     &      ' CF1=',TRANSFER(CF1,1),
     &      ' DI1=',TRANSFER(DI1,1),
     &      ' US1=',TRANSFER(US1,1),
     &      ' T1=',TRANSFER(T1,1)
           WRITE(0,'(A,I1,A,I2,A,I2,A,Z8,A,Z8,A,Z8,A,Z8,A,Z8,A,Z8)')
     &      'F_SIMI_COM2 S=',IS,' C=',MRCHDU_COUNT,' it=',ITBL,
     &      ' HK2=',TRANSFER(HK2,1),
     &      ' HS2=',TRANSFER(HS2,1),
     &      ' CF2=',TRANSFER(CF2,1),
     &      ' DI2=',TRANSFER(DI2,1),
     &      ' US2=',TRANSFER(US2,1),
     &      ' T2=',TRANSFER(T2,1)
          ENDIF
C-------- trace BLKIN at station 5 side 2 during all MRCHDU calls
          IF(IS.EQ.2 .AND. IBL.EQ.5) THEN
           WRITE(0,'(A,I2,A,I2,A,Z8,A,Z8,A,Z8,A,Z8,
     &      A,Z8,A,Z8,A,Z8,A,Z8)')
     &      'F_BK5 mdu=',MRCHDU_COUNT,' it=',ITBL,
     &      ' HK=',TRANSFER(HK2,1),
     &      ' RT=',TRANSFER(RT2,1),
     &      ' HS=',TRANSFER(HS2,1),
     &      ' CF=',TRANSFER(CF2,1),
     &      ' DI=',TRANSFER(DI2,1),
     &      ' T2=',TRANSFER(T2,1),
     &      ' D2=',TRANSFER(D2,1),
     &      ' U2=',TRANSFER(U2,1)
          ENDIF
C-------- trace BLKIN at side 2 station 4, first MRCHDU only
          IF(IS.EQ.2 .AND. IBL.EQ.4 .AND. ITBL.EQ.1
     &       .AND. MRCHDU_COUNT.LE.10) THEN
           WRITE(*,'(A,Z8,1X,Z8,1X,Z8,1X,Z8,1X,Z8,1X,Z8)')
     &      'F_BLKIN24',
     &      TRANSFER(HK2,1),TRANSFER(RT2,1),
     &      TRANSFER(H2,1),TRANSFER(HS2,1),
     &      TRANSFER(THI,1),TRANSFER(DSI,1)
           WRITE(*,'(A,Z8,1X,Z8,1X,Z8,1X,Z8,1X,Z8)')
     &      'F_BLK24B',
     &      TRANSFER(X1,1),TRANSFER(X2,1),
     &      TRANSFER(T1,1),TRANSFER(T2,1),
     &      TRANSFER(D2,1)
          ENDIF
C
C-------- check for transition and set appropriate flags and things
          IF(IS.EQ.2 .AND. IBL.EQ.24
     &       .AND. MRCHDU_COUNT.EQ.22) THEN
           WRITE(0,'(A,I2,A,L1,A,L1,A,L1,A,Z8,A,Z8,A,Z8)')
     &      'F_MDU22_FLAGS it=',ITBL,' simi=',SIMI,
     &      ' turb=',TURB,' tran=',TRAN,
     &      ' AMI=',TRANSFER(AMI,1),
     &      ' AMPL1=',TRANSFER(AMPL1,1),
     &      ' AMPL2=',TRANSFER(AMPL2,1)
          ENDIF
C-------- set TRACE_* before TRCHEK so inner gate works
          TRACE_SIDE = IS
          TRACE_STATION = IBL
          TRACE_ITER = ITBL
          IF((.NOT.SIMI) .AND. (.NOT.TURB)) THEN
           CALL TRCHEK
           AMI = AMPL2
           IF(IS.EQ.2 .AND. IBL.EQ.24
     &        .AND. MRCHDU_COUNT.EQ.22 .AND. ITBL.EQ.4) THEN
            WRITE(0,'(A,6(A,Z8))')
     &       'F_TR22_OUT it=4',
     &       ' XT=',TRANSFER(XT,1),
     &       ' TT=',TRANSFER(TT,1),
     &       ' DT=',TRANSFER(DT,1),
     &       ' UT=',TRANSFER(UT,1),
     &       ' A2=',TRANSFER(AMPL2,1),
     &       ' WF2=',TRANSFER(WF2,1)
           ENDIF
C---- GDB: MRCHDU TRCHEK amplification trace for parity comparison
           IF(ITBL.EQ.1 .AND. IBL.GE.2 .AND. IBL.LE.60) THEN
            WRITE(0,'(A,I1,A,I3,A,Z8,A,Z8,A,Z8,A,Z8,A,L1,A,Z8)')
     &       'F_MDU_TR S=',IS,' I=',IBL,
     &       ' A1=',TRANSFER(AMPL1,1),
     &       ' A2=',TRANSFER(AMI,1),
     &       ' HK1=',TRANSFER(HK1,1),
     &       ' HK2=',TRANSFER(HK2,1),
     &       ' T=',TRAN,
     &       ' XT=',TRANSFER(XT,1)
           ENDIF
           IF(     TRAN) ITRAN(IS) = IBL
           IF(.NOT.TRAN) ITRAN(IS) = IBL+2
           IF(IS.EQ.1 .AND. IBL.GE.3 .AND. IBL.LE.11
     &        .AND. MRCHDU_COUNT.EQ.20) THEN
            WRITE(0,'(A,I3,A,I2,A,L1,A,Z8,A,Z8,A,Z8,A,I3)')
     &       'F_PFCM_TR mc=20 ibl=',IBL,' iter=',ITBL,' tran=',TRAN,
     &       ' A1=',TRANSFER(AMPL1,1),
     &       ' A2=',TRANSFER(AMI,1),
     &       ' XT=',TRANSFER(XT,1),
     &       ' ITRAN->',ITRAN(IS)
           ENDIF
          ENDIF
          IF(IS.EQ.2 .AND. IBL.EQ.58 .AND. ITBL.EQ.1) THEN
           WRITE(0,'(A,L1,A,L1,A,L1,A,I3,A,I3)')
     &      'F_UPD58 TRAN=',TRAN,' TURB=',TURB,' SIMI=',SIMI,
     &      ' ITRAN=',ITRAN(IS),' IBLTE=',IBLTE(IS)
          ENDIF
C
          IF(IBL.EQ.IBLTE(IS)+1) THEN
           TTE = THET(IBLTE(1),1) + THET(IBLTE(2),2)
           DTE = DSTR(IBLTE(1),1) + DSTR(IBLTE(2),2) + ANTE
           CTE = ( CTAU(IBLTE(1),1)*THET(IBLTE(1),1)
     &           + CTAU(IBLTE(2),2)*THET(IBLTE(2),2) ) / TTE
           IF(IS.EQ.2 .AND. MRCHDU_COUNT.EQ.1) THEN
            WRITE(0,'(A,I2,A,Z8,A,Z8,A,Z8,A,Z8,A,Z8,A,Z8,A,Z8)')
     &       'F_MDU_CTE mc=',MRCHDU_COUNT,
     &       ' CTE=',TRANSFER(CTE,1),
     &       ' TTE=',TRANSFER(TTE,1),
     &       ' DTE=',TRANSFER(DTE,1),
     &       ' C1=',TRANSFER(CTAU(IBLTE(1),1),1),
     &       ' T1=',TRANSFER(THET(IBLTE(1),1),1),
     &       ' C2=',TRANSFER(CTAU(IBLTE(2),2),1),
     &       ' T2=',TRANSFER(THET(IBLTE(2),2),1)
           ENDIF
           CALL TESYS(CTE,TTE,DTE)
          ELSE
           TRACE_SIDE = IS
           TRACE_STATION = IBL
           TRACE_ITER = ITBL
           IF(IS.EQ.2 .AND. IBL.EQ.83 .AND. ITBL.EQ.1) THEN
            WRITE(0,783) MRCHDU_COUNT,
     &       TRANSFER(HK1,1),TRANSFER(DI1,1),
     &       TRANSFER(HS1,1),TRANSFER(US1,1)
 783        FORMAT('F_COM183 C=',I1,4(1X,Z8.8))
           ENDIF
           IF(IS.EQ.2 .AND. (IBL.EQ.50.OR.IBL.EQ.85
     &        .OR.IBL.EQ.86) .AND. ITBL.EQ.1) THEN
            WRITE(0,'(A,I3,A,Z8,A,Z8,A,Z8,A,Z8,A,Z8,A,Z8)')
     &       'F_COM1 I=',IBL,' HK1=',TRANSFER(HK1,1),
     &       ' RT1=',TRANSFER(RT1,1),
     &       ' CF1=',TRANSFER(CF1,1),
     &       ' DI1=',TRANSFER(DI1,1),
     &       ' HS1=',TRANSFER(HS1,1),
     &       ' US1=',TRANSFER(US1,1)
           ENDIF
           IF(IS.EQ.2 .AND. IBL.EQ.103 .AND.
     &        MRCHDU_COUNT.EQ.3 .AND.
     &        (ITBL.EQ.1.OR.ITBL.EQ.6)) THEN
            WRITE(*,'(A,I2,A,Z8,A,Z8,A,Z8,A,Z8,A,Z8)')
     &       'F_COM103_',ITBL,
     &       ' S1=',TRANSFER(S1,1),
     &       ' S2=',TRANSFER(S2,1),
     &       ' T2=',TRANSFER(T2,1),
     &       ' D2=',TRANSFER(D2,1),
     &       ' U2=',TRANSFER(U2,1)
           ENDIF
           CALL BLSYS(3)
          ENDIF
C-------- Trace COM2 secondary at IBL=91 every iter in MC=3 (after BLSYS)
          IF(IS.EQ.2 .AND. IBL.EQ.91 .AND. MRCHDU_COUNT.EQ.3) THEN
           WRITE(0,'(A,I2,8(A,Z8))')
     &      'F_STORE91 it=',ITBL,
     &      ' CF=',TRANSFER(CF2,1),
     &      ' DI=',TRANSFER(DI2,1),
     &      ' HS=',TRANSFER(HS2,1),
     &      ' US=',TRANSFER(US2,1),
     &      ' CQ=',TRANSFER(CQ2,1),
     &      ' T=',TRANSFER(THI,1),
     &      ' D=',TRANSFER(DSI,1),
     &      ' U=',TRANSFER(UEI,1)
          ENDIF
C---- BLSYS output at station 27 side 1 during oi=8 (moved to after row4)
          IF(.FALSE. .AND. IS.EQ.1 .AND. IBL.EQ.27
     &       .AND. TRACE_OUTER.EQ.8) THEN
           IVSH27 = 0
           DO 9631 JJR=1, 3
             DO 9632 JJC=1, 4
               IVSH27 = IVSH27 + IAND(TRANSFER(VS2(JJR,JJC),1),
     &                                2147483647)
 9632        CONTINUE
 9631      CONTINUE
           WRITE(0,'(A,I2,9(A,Z8))')
     &      'F_BL27 it=',ITBL,
     &      ' R0=',TRANSFER(VSREZ(1),1),
     &      ' R1=',TRANSFER(VSREZ(2),1),
     &      ' R2=',TRANSFER(VSREZ(3),1),
     &      ' VS=',IVSH27,
     &      ' R3=',TRANSFER(VSREZ(4),1),
     &      ' M41=',TRANSFER(VS2(4,1),1),
     &      ' M42=',TRANSFER(VS2(4,2),1),
     &      ' M43=',TRANSFER(VS2(4,3),1),
     &      ' M44=',TRANSFER(VS2(4,4),1)
          ENDIF
C---- trace BLSYS output at similarity station for MRCHDU call 1
          IF(IBL.EQ.2 .AND. ITBL.LE.3 .AND. MRCHDU_COUNT.LE.2) THEN
           WRITE(0,'(A,I1,A,I2,A,I2,4(1X,Z8))')
     &      'F_SIMI_RES S=',IS,' C=',MRCHDU_COUNT,' it=',ITBL,
     &      TRANSFER(VSREZ(1),1),TRANSFER(VSREZ(2),1),
     &      TRANSFER(VSREZ(3),1),TRANSFER(VSREZ(4),1)
           DO 9801 JJ=1, 4
            WRITE(0,'(A,I1,A,I2,A,I2,A,I1,4(1X,Z8))')
     &       'F_SIMI_VS2 S=',IS,' C=',MRCHDU_COUNT,' it=',ITBL,
     &       ' r',JJ,
     &       TRANSFER(VS2(JJ,1),1),TRANSFER(VS2(JJ,2),1),
     &       TRANSFER(VS2(JJ,3),1),TRANSFER(VS2(JJ,4),1)
 9801      CONTINUE
          ENDIF
C---- VS2 trace at station 7 side 2 MRCHDU call 2 iter 1
          IF(IS.EQ.2 .AND. IBL.EQ.7 .AND. MRCHDU_COUNT.EQ.2
     &       .AND. ITBL.EQ.1) THEN
           DO 9501 JJ=1, 3
            WRITE(*,'(A,I1,1X,Z8,1X,Z8,1X,Z8,1X,Z8)')
     &       'F_VS7r',JJ,
     &       TRANSFER(VS2(JJ,1),1),TRANSFER(VS2(JJ,2),1),
     &       TRANSFER(VS2(JJ,3),1),TRANSFER(VS2(JJ,4),1)
 9501      CONTINUE
           WRITE(*,'(A,Z8,1X,Z8,1X,Z8,1X,Z8,1X,Z8,1X,Z8)')
     &      'F_COM1_7',
     &      TRANSFER(HK1,1),TRANSFER(RT1,1),
     &      TRANSFER(CF1,1),TRANSFER(DI1,1),
     &      TRANSFER(HS1,1),TRANSFER(US1,1)
          ENDIF
C---- trace Newton iter at s2 stn5+6 call 2 and stn5 call 4
          IF(IS.EQ.2 .AND. (IBL.EQ.5.OR.IBL.EQ.6.OR.IBL.EQ.7) .AND.
     &       MRCHDU_COUNT.EQ.2 .AND. ITBL.LE.25) THEN
           WRITE(*,'(A,I1,A,I2,1X,Z8,1X,Z8,1X,Z8,1X,Z8)')
     &      'F_NW',IBL,'_',ITBL,
     &      TRANSFER(THI,1),TRANSFER(DSI,1),
     &      TRANSFER(UEI,1),TRANSFER(HK2,1)
          ENDIF
          IF(IS.EQ.2 .AND. IBL.EQ.5 .AND.
     &       MRCHDU_COUNT.EQ.4 .AND. ITBL.LE.25) THEN
           WRITE(*,'(A,I2,1X,Z8,1X,Z8,1X,Z8,1X,Z8)')
     &      'F_NW5C4_',ITBL,
     &      TRANSFER(THI,1),TRANSFER(DSI,1),
     &      TRANSFER(UEI,1),TRANSFER(CTI,1)
          ENDIF
C---- Trace AMI at every station side 2 call 4
          IF(IS.EQ.2 .AND. MRCHDU_COUNT.EQ.4 .AND.
     &       ITBL.EQ.1 .AND. IBL.GE.4 .AND. IBL.LE.17) THEN
           WRITE(*,'(A,I3,1X,Z8.8,1X,L1,1X,L1)')
     &      'F_AMI4',IBL,TRANSFER(AMI,1),TRAN,TURB
          ENDIF
          IF(IS.EQ.2 .AND. IBL.EQ.16 .AND.
     &       MRCHDU_COUNT.EQ.4 .AND. ITBL.EQ.1) THEN
           WRITE(*,'(A,4(1X,Z8.8))')
     &      'F_IN16C4',
     &      TRANSFER(THET(IBM,IS),1),
     &      TRANSFER(DSTR(IBM,IS),1),
     &      TRANSFER(UEDG(IBM,IS),1),
     &      TRANSFER(CTAU(IBM,IS),1)
           WRITE(*,'(A,7(1X,Z8.8))')
     &      'F_COM16',
     &      TRANSFER(HK1,1),TRANSFER(RT1,1),
     &      TRANSFER(HS1,1),TRANSFER(CF1,1),
     &      TRANSFER(CQ1,1),TRANSFER(DI1,1),
     &      TRANSFER(US1,1)
           WRITE(*,'(A,4(1X,Z8.8),1X,L1,1X,L1)')
     &      'F_IN16b',
     &      TRANSFER(AMPL1,1),TRANSFER(AMPL2,1),
     &      TRANSFER(X1,1),TRANSFER(X2,1),
     &      TRAN,TURB
           WRITE(*,'(A,9(1X,Z8.8))')
     &      'F_KIN15',
     &      TRANSFER(HK1,1),TRANSFER(HK1_T1,1),
     &      TRANSFER(HK1_D1,1),TRANSFER(RT1,1),
     &      TRANSFER(RT1_T1,1),TRANSFER(M1,1),
     &      TRANSFER(H1,1),TRANSFER(H1_T1,1),
     &      TRANSFER(H1_D1,1)
           IDSH = 0
           IDSH = IEOR(IDSH, TRANSFER(HS1_T1,1))
           IDSH = IEOR(IDSH, ISHFT(TRANSFER(HS1_D1,1),1))
           IDSH = IEOR(IDSH, TRANSFER(HS1_U1,1))
           IDSH = IEOR(IDSH, ISHFT(TRANSFER(HS1_MS,1),2))
           IDSH = IEOR(IDSH, TRANSFER(CF1_T1,1))
           IDSH = IEOR(IDSH, ISHFT(TRANSFER(CF1_D1,1),3))
           IDSH = IEOR(IDSH, TRANSFER(CF1_U1,1))
           IDSH = IEOR(IDSH, TRANSFER(CF1_MS,1))
           IDSH = IEOR(IDSH, ISHFT(TRANSFER(DI1_T1,1),4))
           IDSH = IEOR(IDSH, TRANSFER(DI1_D1,1))
           IDSH = IEOR(IDSH, ISHFT(TRANSFER(DI1_U1,1),5))
           IDSH = IEOR(IDSH, TRANSFER(DI1_MS,1))
           IDSH = IEOR(IDSH, TRANSFER(US1_T1,1))
           IDSH = IEOR(IDSH, ISHFT(TRANSFER(US1_D1,1),6))
           IDSH = IEOR(IDSH, TRANSFER(US1_U1,1))
           IDSH = IEOR(IDSH, TRANSFER(US1_MS,1))
           IDSH = IEOR(IDSH, ISHFT(TRANSFER(CQ1_T1,1),7))
           IDSH = IEOR(IDSH, TRANSFER(CQ1_D1,1))
           IDSH = IEOR(IDSH, TRANSFER(CQ1_U1,1))
           IDSH = IEOR(IDSH, TRANSFER(CQ1_MS,1))
           IDSH = IEOR(IDSH, TRANSFER(DE1,1))
           IDSH = IEOR(IDSH, TRANSFER(HC1,1))
           WRITE(*,'(A,Z8.8)')
     &      'F_SEC15_DHASH ',IDSH
          ENDIF
          IF(IS.EQ.2 .AND. IBL.EQ.16 .AND.
     &       MRCHDU_COUNT.EQ.4 .AND. ITBL.EQ.1) THEN
           DO 9815 JJR=1, 3
            WRITE(0,'(A,I1,A,Z8,1X,Z8,1X,Z8,1X,Z8,A,Z8)')
     &       'F_VS2_16 r',JJR-1,':',
     &       TRANSFER(VS2(JJR,1),1),
     &       TRANSFER(VS2(JJR,2),1),
     &       TRANSFER(VS2(JJR,3),1),
     &       TRANSFER(VS2(JJR,4),1),
     &       ' |',TRANSFER(VSREZ(JJR),1)
 9815      CONTINUE
          ENDIF
          IF(IS.EQ.2 .AND. IBL.EQ.16 .AND.
     &       MRCHDU_COUNT.EQ.4 .AND. ITBL.LE.25) THEN
           WRITE(*,'(A,I2,1X,Z8,1X,Z8,1X,Z8,1X,Z8)')
     &      'F_NW16C4',ITBL,
     &      TRANSFER(THI,1),TRANSFER(DSI,1),
     &      TRANSFER(UEI,1),TRANSFER(CTI,1)
          ENDIF
C---- trace Newton iter at s2 wake stn103 call 3
          IF(IS.EQ.2 .AND. IBL.EQ.103 .AND.
     &       MRCHDU_COUNT.EQ.3 .AND. ITBL.LE.25) THEN
           WRITE(*,'(A,I2,1X,Z8,1X,Z8,1X,Z8,1X,Z8)')
     &      'F_WK103_',ITBL,
     &      TRANSFER(THI,1),TRANSFER(DSI,1),
     &      TRANSFER(UEI,1),TRANSFER(CTI,1)
          ENDIF
C---- BLVAR output + raw VS2 at stn103 call 3 iters 1+6
          IF(IS.EQ.2 .AND. IBL.EQ.103 .AND.
     &       MRCHDU_COUNT.EQ.3 .AND.
     &       (ITBL.EQ.1 .OR. ITBL.EQ.6)) THEN
           WRITE(*,'(A,I2,A,Z8,A,Z8,A,Z8,A,Z8,A,Z8,A,Z8)')
     &      'F_BLV103_',ITBL,
     &      ' HK=',TRANSFER(HK2,1),
     &      ' HS=',TRANSFER(HS2,1),
     &      ' US=',TRANSFER(US2,1),
     &      ' CQ=',TRANSFER(CQ2,1),
     &      ' CF=',TRANSFER(CF2,1),
     &      ' DI=',TRANSFER(DI2,1)
          ENDIF
          IF(IS.EQ.2 .AND. IBL.EQ.103 .AND.
     &       MRCHDU_COUNT.EQ.3 .AND.
     &       (ITBL.EQ.1 .OR. ITBL.EQ.6)) THEN
           DO 9602 JJR=1, 3
            WRITE(*,'(A,I1,A,Z8,1X,Z8,1X,Z8,1X,Z8,A,Z8)')
     &       'F_RAW103 r',JJR-1,':',
     &       TRANSFER(VS2(JJR,1),1),
     &       TRANSFER(VS2(JJR,2),1),
     &       TRANSFER(VS2(JJR,3),1),
     &       TRANSFER(VS2(JJR,4),1),
     &       ' |',TRANSFER(VSREZ(JJR),1)
 9602      CONTINUE
          ENDIF
C
C-------- set stuff at first iteration...
          IF(ITBL.EQ.1) THEN
C
C--------- set "baseline" Ue and Hk for forming  Ue(Hk)  relation
           UEREF = U2
           HKREF = HK2
C
C--------- if current point IBL was turbulent and is now laminar, then...
           IF(IBL.LT.ITRAN(IS) .AND. IBL.GE.ITROLD ) THEN
C---------- extrapolate baseline Hk
            UEM = UEDG(IBL-1,IS)
            DSM = DSTR(IBL-1,IS)
            THM = THET(IBL-1,IS)
            MSQ = UEM*UEM*HSTINV / (GM1BL*(1.0 - 0.5*UEM*UEM*HSTINV))
            CALL HKIN( DSM/THM, MSQ, HKREF, DUMMY, DUMMY )
           ENDIF
C
C--------- if current point IBL was laminar, then...
           IF(IBL.LT.ITROLD) THEN
C---------- reinitialize or extrapolate Ctau if it's now turbulent
            IF(TRAN) CTAU(IBL,IS) = 0.03
            IF(TURB) CTAU(IBL,IS) = CTAU(IBL-1,IS)
            IF(TRAN .OR. TURB) THEN
             CTI = CTAU(IBL,IS)
             S2 = CTI
            ENDIF
           ENDIF
C
          ENDIF
C
C
          IF(SIMI .OR. IBL.EQ.IBLTE(IS)+1) THEN
C
C--------- for similarity station or first wake point, prescribe Ue
           VS2(4,1) = 0.
           VS2(4,2) = 0.
           VS2(4,3) = 0.
           VS2(4,4) = U2_UEI
           VSREZ(4) = UEREF - U2
C
          ELSE
C
C********* calculate Ue-Hk characteristic slope
C
           DO 20 K=1, 4
             VZTMP(K) = VSREZ(K)
             DO 201 L=1, 5
               VTMP(K,L) = VS2(K,L)
  201        CONTINUE
   20      CONTINUE
C
C--------- set unit dHk
           VTMP(4,1) = 0.
           VTMP(4,2) = HK2_T2
           VTMP(4,3) = HK2_D2
           VTMP(4,4) = HK2_U2*U2_UEI
           VZTMP(4)  = 1.0
C---- n6h20 CHMAT trace BEFORE GAUSS at IBL=66 MRCHDU_COUNT=10
           IF(IS.EQ.2 .AND. IBL.EQ.66 .AND. MRCHDU_COUNT.EQ.10
     &        .AND. ITBL.LE.3) THEN
            WRITE(0,'(A,I2,A,Z8,A,Z8,A,Z8,A,Z8)')
     &       'F_CHRHS10_66 it=',ITBL,
     &       ' R0=',TRANSFER(VZTMP(1),1),
     &       ' R1=',TRANSFER(VZTMP(2),1),
     &       ' R2=',TRANSFER(VZTMP(3),1),
     &       ' R3=',TRANSFER(VZTMP(4),1)
            DO 9711 JJC=1, 4
             WRITE(0,'(A,I2,A,I1,A,Z8,A,Z8,A,Z8,A,Z8)')
     &        'F_CHMAT10_66 it=',ITBL,' r',JJC-1,
     &        ' M0=',TRANSFER(VTMP(JJC,1),1),
     &        ' M1=',TRANSFER(VTMP(JJC,2),1),
     &        ' M2=',TRANSFER(VTMP(JJC,3),1),
     &        ' M3=',TRANSFER(VTMP(JJC,4),1)
 9711       CONTINUE
           ENDIF
C
C--------- calculate dUe response
           IF(IS.EQ.2 .AND. IBL.EQ.5 .AND. MRCHDU_COUNT.EQ.2
     &        .AND. ITBL.EQ.6) THEN
            DO 9191 JJJ=1, 4
             WRITE(*,'(A,I1,1X,Z8,1X,Z8,1X,Z8,1X,Z8,A,Z8)')
     &        'F_PR',JJJ,
     &        TRANSFER(VTMP(JJJ,1),1),TRANSFER(VTMP(JJJ,2),1),
     &        TRANSFER(VTMP(JJJ,3),1),TRANSFER(VTMP(JJJ,4),1),
     &        ' |',TRANSFER(VZTMP(JJJ),1)
 9191       CONTINUE
           ENDIF
           IF(IS.EQ.2 .AND. IBL.EQ.24
     &        .AND. MRCHDU_COUNT.EQ.22 .AND. ITBL.EQ.4) THEN
            DO 9192 JJJ=1, 4
             WRITE(0,'(A,I1,1X,Z8,1X,Z8,1X,Z8,1X,Z8,A,Z8)')
     &        'F_MDU22_PMAT it=4 r',JJJ-1,
     &        TRANSFER(VTMP(JJJ,1),1),TRANSFER(VTMP(JJJ,2),1),
     &        TRANSFER(VTMP(JJJ,3),1),TRANSFER(VTMP(JJJ,4),1),
     &        ' |',TRANSFER(VZTMP(JJJ),1)
 9192       CONTINUE
            WRITE(0,'(A,A,Z8,A,Z8,A,Z8,A,Z8)')
     &       'F_MDU22_KIN1 it=4',
     &       ' HK1=',TRANSFER(HK1,1),' RT1=',TRANSFER(RT1,1),
     &       ' HK_T=',TRANSFER(HK1_T1,1),
     &       ' HK_D=',TRANSFER(HK1_D1,1)
            WRITE(0,'(A,A,Z8,A,Z8,A,Z8,A,Z8,A,Z8)')
     &       'F_MDU22_SEC1 it=4',
     &       ' CF1=',TRANSFER(CF1,1),' DI1=',TRANSFER(DI1,1),
     &       ' HS1=',TRANSFER(HS1,1),' US1=',TRANSFER(US1,1),
     &       ' CQ1=',TRANSFER(CQ1,1)
           ENDIF
           CALL GAUSS(4,4,VTMP,VZTMP,1)
C---- n6h20 SENS debug: trace VZTMP(4) at IBL=66 MRCHDU_COUNT=10
           IF(IS.EQ.2 .AND. IBL.EQ.66 .AND. MRCHDU_COUNT.EQ.10
     &        .AND. ITBL.LE.3) THEN
            WRITE(0,'(A,I2,A,Z8,A,Z8,A,Z8)')
     &       'F_CHAR10_66 it=',ITBL,
     &       ' VZ4=',TRANSFER(VZTMP(4),1),
     &       ' HKREF=',TRANSFER(HKREF,1),
     &       ' UEREF=',TRANSFER(UEREF,1)
            WRITE(0,'(A,I2,A,Z8,A,Z8,A,Z8,A,Z8,A,Z8)')
     &       'F_HK10_66 it=',ITBL,
     &       ' HK2_T2=',TRANSFER(HK2_T2,1),
     &       ' HK2_D2=',TRANSFER(HK2_D2,1),
     &       ' HK2_U2=',TRANSFER(HK2_U2,1),
     &       ' U2_UEI=',TRANSFER(U2_UEI,1),
     &       ' HK2=',TRANSFER(HK2,1)
            WRITE(0,'(A,I2,A,Z8,A,Z8,A,Z8,A,Z8,A,Z8,A,Z8)')
     &       'F_SEC10_66 it=',ITBL,
     &       ' CF2=',TRANSFER(CF2,1),
     &       ' DI2=',TRANSFER(DI2,1),
     &       ' HS2=',TRANSFER(HS2,1),
     &       ' US2=',TRANSFER(US2,1),
     &       ' CQ2=',TRANSFER(CQ2,1),
     &       ' DE2=',TRANSFER(DE2,1)
           ENDIF
C
C--------- set  SENSWT * (normalized dUe/dHk)
           SENNEW = SENSWT * VZTMP(4) * HKREF/UEREF
           IF(ITBL.LE.25) THEN
            SENS = SENNEW
           ELSE IF(ITBL.LE.15) THEN
            SENS = 0.5*(SENS + SENNEW)
           ENDIF
           IF(IS.EQ.2 .AND. IBL.EQ.24 .AND. MRCHDU_COUNT.EQ.22) THEN
            WRITE(0,'(A,I2,6(A,Z8))')
     &       'F_MDU22_SENS it=',ITBL,
     &       ' senNew=',TRANSFER(SENNEW,1),
     &       ' cDelta3=',TRANSFER(VZTMP(4),1),
     &       ' hkref=',TRANSFER(HKREF,1),
     &       ' ueref=',TRANSFER(UEREF,1),
     &       ' hk2C=',TRANSFER(HK2,1),
     &       ' cu2=',TRANSFER(U2,1)
           ENDIF
C---- n6h20 SENS debug: trace SENS at IBL=66 MRCHDU_COUNT=10
           IF(IS.EQ.2 .AND. IBL.EQ.66 .AND. MRCHDU_COUNT.EQ.10
     &        .AND. ITBL.LE.3) THEN
            WRITE(0,'(A,I2,A,Z8,A,Z8)')
     &       'F_SENS10_66 it=',ITBL,
     &       ' SENNEW=',TRANSFER(SENNEW,1),
     &       ' SENS=',TRANSFER(SENS,1)
           ENDIF
C
C--------- set prescribed Ue-Hk combination
           VS2(4,1) = 0.
           VS2(4,2) =  HK2_T2 * HKREF
           VS2(4,3) =  HK2_D2 * HKREF
           VS2(4,4) =( HK2_U2 * HKREF  +  SENS/UEREF )*U2_UEI
           VSREZ(4) = -(HKREF**2)*(HK2 / HKREF - 1.0)
     &                     - SENS*(U2  / UEREF - 1.0)
C---- n6h20 Newton debug: trace VS2 row 4 and VSREZ(4) at IBL=66 MRCHDU_COUNT=10
           IF(IS.EQ.2 .AND. IBL.EQ.66 .AND. MRCHDU_COUNT.EQ.10
     &        .AND. ITBL.LE.3) THEN
            WRITE(0,'(A,I2,A,Z8,A,Z8,A,Z8,A,Z8,A,Z8)')
     &       'F_VS4_10_66 it=',ITBL,
     &       ' VS41=',TRANSFER(VS2(4,1),1),
     &       ' VS42=',TRANSFER(VS2(4,2),1),
     &       ' VS43=',TRANSFER(VS2(4,3),1),
     &       ' VS44=',TRANSFER(VS2(4,4),1),
     &       ' VSR4=',TRANSFER(VSREZ(4),1)
           ENDIF
C
          ENDIF
C
C---- GDB: BEFORE GAUSS - raw residuals
          IF(IS.EQ.1 .AND. IBL.EQ.58 .AND. ITBL.EQ.1) THEN
           WRITE(*,'(A,16(1X,Z8))')
     &      'F_SYS58',
     &      TRANSFER(VS2(1,1),1),TRANSFER(VS2(1,2),1),
     &      TRANSFER(VS2(1,3),1),TRANSFER(VS2(1,4),1),
     &      TRANSFER(VS2(2,1),1),TRANSFER(VS2(2,2),1),
     &      TRANSFER(VS2(2,3),1),TRANSFER(VS2(2,4),1),
     &      TRANSFER(VS2(3,1),1),TRANSFER(VS2(3,2),1),
     &      TRANSFER(VS2(3,3),1),TRANSFER(VS2(3,4),1),
     &      TRANSFER(VS2(4,1),1),TRANSFER(VS2(4,2),1),
     &      TRANSFER(VS2(4,3),1),TRANSFER(VS2(4,4),1)
           WRITE(*,'(A,4(1X,Z8))')
     &      'F_RHS58',
     &      TRANSFER(VSREZ(1),1),TRANSFER(VSREZ(2),1),
     &      TRANSFER(VSREZ(3),1),TRANSFER(VSREZ(4),1)
          ENDIF
          IF(WAKE) THEN
           WRITE(0,'(A,I2,A,I3,A,Z8,A,Z8,A,Z8,A,Z8)')
     &      'F_MRCHDU_RAW IS=',IS,' IBL=',IBL,
     &      ' R0=',TRANSFER(VSREZ(1),1),
     &      ' R1=',TRANSFER(VSREZ(2),1),
     &      ' R2=',TRANSFER(VSREZ(3),1),
     &      ' HK=',TRANSFER(HK2,1)
           WRITE(0,'(A,I2,A,I3,A,Z8,A,Z8,A,Z8,A,Z8)')
     &      'F_MRCHDU_BLV IS=',IS,' IBL=',IBL,
     &      ' HS=',TRANSFER(HS2,1),
     &      ' CF=',TRANSFER(CF2,1),
     &      ' CQ=',TRANSFER(CQ2,1),
     &      ' US=',TRANSFER(US2,1)
          ENDIF
          IF((IS.EQ.1 .AND. (IBL.EQ.3.OR.IBL.EQ.4)
     &      .AND. ITBL.EQ.1)
     &     .OR. (IS.EQ.2.AND.IBL.EQ.83.AND.ITBL.EQ.1)) THEN
            DO 9892 JJR=1, 4
              WRITE(*,'(A,I2,A,I1,A,Z8,1X,Z8,1X,Z8,1X,Z8,A,Z8)')
     &         'F_S3i',ITBL,' r',JJR-1,':',
     &         TRANSFER(VS2(JJR,1),1),
     &         TRANSFER(VS2(JJR,2),1),
     &         TRANSFER(VS2(JJR,3),1),
     &         TRANSFER(VS2(JJR,4),1),
     &         ' |',TRANSFER(VSREZ(JJR),1)
 9892       CONTINUE
            WRITE(*,'(A,5(1X,Z8.8))')
     &       'F_DU59HK',
     &       TRANSFER(HK2_U2,1),
     &       TRANSFER(HKREF,1),
     &       TRANSFER(SENS,1),
     &       TRANSFER(UEREF,1),
     &       TRANSFER(U2_UEI,1)
          ENDIF
C-------- solve Newton system for current "2" station
          IF(IS.EQ.2 .AND. (IBL.EQ.50.OR.IBL.EQ.85
     &       .OR.IBL.EQ.86) .AND. ITBL.EQ.1) THEN
           WRITE(0,'(A,I3,3(1X,Z8),A,3(1X,Z8))')
     &      'F_REZ I=',IBL,(TRANSFER(VSREZ(K),1),K=1,3),
     &      ' V14=',TRANSFER(VS2(1,4),1),
     &      ' V24=',TRANSFER(VS2(2,4),1),
     &      ' V34=',TRANSFER(VS2(3,4),1)
          ENDIF
          IF(IS.EQ.2 .AND. IBL.EQ.5) THEN
           DO 9895 JJR=1, 4
             WRITE(0,'(A,I2,A,I1,A,Z8,1X,Z8,1X,Z8,1X,Z8,
     &        A,Z8)')
     &        'F_G5 it=',ITBL,' r',JJR-1,':',
     &        TRANSFER(VS2(JJR,1),1),
     &        TRANSFER(VS2(JJR,2),1),
     &        TRANSFER(VS2(JJR,3),1),
     &        TRANSFER(VS2(JJR,4),1),
     &        ' |',TRANSFER(VSREZ(JJR),1)
 9895      CONTINUE
          ENDIF
          IF(IS.EQ.2 .AND. IBL.EQ.83
     &       .AND. MRCHDU_COUNT.LE.1) THEN
           DO 9896 JJR=1, 4
            WRITE(0,'(A,I2,A,I1,A,Z8,1X,Z8,1X,Z8,1X,Z8,
     &       A,Z8)')
     &       'F_G83 it=',ITBL,' r',JJR-1,':',
     &       TRANSFER(VS2(JJR,1),1),
     &       TRANSFER(VS2(JJR,2),1),
     &       TRANSFER(VS2(JJR,3),1),
     &       TRANSFER(VS2(JJR,4),1),
     &       ' |',TRANSFER(VSREZ(JJR),1)
 9896      CONTINUE
          ENDIF
          IF(IS.EQ.2 .AND. IBL.GE.84 .AND. IBL.LE.93
     &       .AND. MRCHDU_COUNT.EQ.2 .AND. ITBL.EQ.1) THEN
           WRITE(0,'(A,I3,A,Z8)')
     &      'F_SENS i=',IBL,' S=',TRANSFER(SENS,1)
          ENDIF
          IF(IS.EQ.2 .AND. IBL.GE.84 .AND. IBL.LE.93
     &       .AND. MRCHDU_COUNT.EQ.2 .AND. ITBL.EQ.1) THEN
           DO 9655 JJR=1, 4
            WRITE(0,'(A,I3,A,I2,A,I1,A,Z8,1X,Z8,1X,Z8,1X,Z8,'//
     &       'A,Z8)')
     &       'F_MATW',IBL,' it=',ITBL,' r',JJR-1,':',
     &       TRANSFER(VS2(JJR,1),1),
     &       TRANSFER(VS2(JJR,2),1),
     &       TRANSFER(VS2(JJR,3),1),
     &       TRANSFER(VS2(JJR,4),1),
     &       ' |',TRANSFER(VSREZ(JJR),1)
 9655      CONTINUE
          ENDIF
          IF(IS.EQ.2 .AND. IBL.EQ.93
     &       .AND. MRCHDU_COUNT.EQ.2
     &       .AND. (ITBL.EQ.1.OR.ITBL.EQ.6.OR.ITBL.EQ.7)) THEN
           DO 9651 JJR=1, 4
            WRITE(0,'(A,I2,A,I1,A,Z8,1X,Z8,1X,Z8,1X,Z8,'//
     &       'A,Z8)')
     &       'F_SYS93 it=',ITBL,' r',JJR-1,':',
     &       TRANSFER(VS2(JJR,1),1),
     &       TRANSFER(VS2(JJR,2),1),
     &       TRANSFER(VS2(JJR,3),1),
     &       TRANSFER(VS2(JJR,4),1),
     &       ' |',TRANSFER(VSREZ(JJR),1)
 9651      CONTINUE
          ENDIF
C-------- station 92 IS=2 MRCHDU_COUNT=3 iter 1: full system before GAUSS
          IF(IS.EQ.2 .AND. IBL.EQ.92
     &       .AND. MRCHDU_COUNT.EQ.3 .AND. ITBL.EQ.1) THEN
           DO 9657 JJR=1, 4
            WRITE(0,'(A,I1,A,Z8,1X,Z8,1X,Z8,1X,Z8,A,Z8)')
     &       'F_M92C3 r',JJR-1,':',
     &       TRANSFER(VS2(JJR,1),1),
     &       TRANSFER(VS2(JJR,2),1),
     &       TRANSFER(VS2(JJR,3),1),
     &       TRANSFER(VS2(JJR,4),1),
     &       ' |',TRANSFER(VSREZ(JJR),1)
 9657      CONTINUE
           WRITE(0,'(A,5(A,Z8))')
     &      'F_M92C3_REF',
     &      ' HKREF=',TRANSFER(HKREF,1),
     &      ' UEREF=',TRANSFER(UEREF,1),
     &      ' SENS=',TRANSFER(SENS,1),
     &      ' HK2=',TRANSFER(HK2,1),
     &      ' U2=',TRANSFER(U2,1)
           WRITE(0,'(A,7(A,Z8))')
     &      'F_M92C3_COM1',
     &      ' T1=',TRANSFER(T1,1),
     &      ' D1=',TRANSFER(D1,1),
     &      ' U1=',TRANSFER(U1,1),
     &      ' HK1=',TRANSFER(HK1,1),
     &      ' RT1=',TRANSFER(RT1,1),
     &      ' M1=',TRANSFER(M1,1),
     &      ' H1=',TRANSFER(H1,1)
           WRITE(0,'(A,7(A,Z8))')
     &      'F_M92C3_SEC1',
     &      ' CF1=',TRANSFER(CF1,1),
     &      ' DI1=',TRANSFER(DI1,1),
     &      ' HS1=',TRANSFER(HS1,1),
     &      ' US1=',TRANSFER(US1,1),
     &      ' CQ1=',TRANSFER(CQ1,1),
     &      ' HC1=',TRANSFER(HC1,1),
     &      ' DE1=',TRANSFER(DE1,1)
          ENDIF
          IF(IS.EQ.1 .AND. IBL.EQ.27 .AND. TRACE_OUTER.EQ.8) THEN
           WRITE(0,'(A,I2,9(A,Z8))')
     &      'F_PRE_G27 it=',ITBL,
     &      ' R0=',TRANSFER(VSREZ(1),1),
     &      ' R3=',TRANSFER(VSREZ(4),1),
     &      ' M41=',TRANSFER(VS2(4,1),1),
     &      ' M42=',TRANSFER(VS2(4,2),1),
     &      ' M43=',TRANSFER(VS2(4,3),1),
     &      ' M44=',TRANSFER(VS2(4,4),1),
     &      ' SNS=',TRANSFER(SENS,1),
     &      ' HKR=',TRANSFER(HKREF,1),
     &      ' UER=',TRANSFER(UEREF,1)
          ENDIF
C---- Iter 12 station 66 matrix+RHS dump (NACA 0021 debug)
          IF(IS.EQ.2 .AND. IBL.EQ.66 .AND. MRCHDU_COUNT.EQ.12
     &       .AND. ITBL.EQ.1) THEN
           DO 9691 JJR=1, 4
            WRITE(0,'(A,I1,A,Z8,1X,Z8,1X,Z8,1X,Z8,A,Z8)')
     &       'F_MAT66_12 r',JJR-1,':',
     &       TRANSFER(VS2(JJR,1),1),TRANSFER(VS2(JJR,2),1),
     &       TRANSFER(VS2(JJR,3),1),TRANSFER(VS2(JJR,4),1),
     &       ' | ',TRANSFER(VSREZ(JJR),1)
 9691      CONTINUE
           WRITE(0,'(A,10(A,Z8))')
     &      'F_IN66_12',
     &      ' T1=',TRANSFER(T1,1),' D1=',TRANSFER(D1,1),
     &      ' U1=',TRANSFER(U1,1),' S1=',TRANSFER(CTAU(IBM,IS),1),
     &      ' T2=',TRANSFER(T2,1),' D2=',TRANSFER(D2,1),
     &      ' U2=',TRANSFER(U2,1),' S2=',TRANSFER(CTI,1),
     &      ' DW1=',TRANSFER(DW1,1),' DW2=',TRANSFER(DSWAKI,1)
           WRITE(0,'(A,A,Z8,A,Z8,A,Z8,A,Z8,A,Z8,A,Z8,A,Z8)')
     &      'F_IN66_12_R',
     &      ' R0=',TRANSFER(VSREZ(1),1),' R1=',TRANSFER(VSREZ(2),1),
     &      ' R2=',TRANSFER(VSREZ(3),1),
     &      ' HK2=',TRANSFER(HK2,1),
     &      ' HKT=',TRANSFER(HK2_T2,1),' HKD=',TRANSFER(HK2_D2,1),
     &      ' HKU=',TRANSFER(HK2_U2,1)
           WRITE(0,'(A,A,Z8,A,Z8,A,Z8,A,Z8,A,Z8,A,Z8,A,Z8)')
     &      'F_COM1_66',
     &      ' HK1=',TRANSFER(HK1,1),' RT1=',TRANSFER(RT1,1),
     &      ' CF1=',TRANSFER(CF1,1),' DI1=',TRANSFER(DI1,1),
     &      ' HS1=',TRANSFER(HS1,1),' US1=',TRANSFER(US1,1),
     &      ' CQ1=',TRANSFER(CQ1,1)
          ENDIF
          IF(IS.EQ.2 .AND. IBL.EQ.24 .AND. MRCHDU_COUNT.EQ.22) THEN
           WRITE(0,'(A,I2,4(A,Z8))')
     &      'F_MDU22_RHS it=',ITBL,
     &      ' rhs0=',TRANSFER(VSREZ(1),1),
     &      ' rhs1=',TRANSFER(VSREZ(2),1),
     &      ' rhs2=',TRANSFER(VSREZ(3),1),
     &      ' rhs3=',TRANSFER(VSREZ(4),1)
          ENDIF
          CALL GAUSS(4,4,VS2,VSREZ,1)
          IF(IS.EQ.2 .AND. IBL.EQ.24 .AND. MRCHDU_COUNT.EQ.22) THEN
           WRITE(0,'(A,I2,4(A,Z8))')
     &      'F_MDU22_DELTA it=',ITBL,
     &      ' d0=',TRANSFER(VSREZ(1),1),
     &      ' d1=',TRANSFER(VSREZ(2),1),
     &      ' d2=',TRANSFER(VSREZ(3),1),
     &      ' d3=',TRANSFER(VSREZ(4),1)
          ENDIF
C---- Iter 12 station 66 post-solve deltas
          IF(IS.EQ.2 .AND. IBL.EQ.66 .AND. MRCHDU_COUNT.EQ.12
     &       .AND. ITBL.EQ.1) THEN
           WRITE(0,'(A,4(1X,Z8))')
     &      'F_DEL66_12 ',
     &      TRANSFER(VSREZ(1),1),TRANSFER(VSREZ(2),1),
     &      TRANSFER(VSREZ(3),1),TRANSFER(VSREZ(4),1)
          ENDIF
C---- moved to after RLX computation
C
C---- GDB: AFTER GAUSS - solved deltas
          IF(IS.EQ.2 .AND. (IBL.EQ.50.OR.IBL.EQ.86)
     &       .AND. ITBL.EQ.1) THEN
           WRITE(0,'(A,I3,A,Z8,A,Z8,A,Z8,A,Z8)')
     &      'F_DEL I=',IBL,' D0=',TRANSFER(VSREZ(1),1),
     &      ' D1=',TRANSFER(VSREZ(2),1),
     &      ' D2=',TRANSFER(VSREZ(3),1),
     &      ' D3=',TRANSFER(VSREZ(4),1)
          ENDIF
          IF(IS.EQ.2 .AND. IBL.EQ.85) THEN
           WRITE(0,'(A,I2,A,I2,A,Z8,A,Z8,A,Z8,A,Z8,A,Z8)')
     &      'F_WK85_DEL c=',MRCHDU_COUNT,' it=',ITBL,
     &      ' d0=',TRANSFER(VSREZ(1),1),
     &      ' d1=',TRANSFER(VSREZ(2),1),
     &      ' d2=',TRANSFER(VSREZ(3),1),
     &      ' d3=',TRANSFER(VSREZ(4),1),
     &      ' WG=',TRANSFER(DSWAKI,1)
          ENDIF
          IF(WAKE) THEN
           WRITE(0,'(A,I2,A,I3,A,Z8,A,Z8,A,Z8,A,Z8)')
     &      'F_MRCHDU_DEL IS=',IS,' IBL=',IBL,
     &      ' D0=',TRANSFER(VSREZ(1),1),
     &      ' D1=',TRANSFER(VSREZ(2),1),
     &      ' D2=',TRANSFER(VSREZ(3),1),
     &      ' D3=',TRANSFER(VSREZ(4),1)
          ENDIF
          IF(IS.EQ.1 .AND. (IBL.EQ.3.OR.IBL.EQ.4)
     &       .AND. ITBL.LE.4) THEN
            WRITE(*,'(A,I2,A,I2,4(1X,Z8.8),A,Z8.8,A,2(1X,Z8.8))')
     &       'F_DEL',IBL,'i',ITBL,
     &       TRANSFER(VSREZ(1),1),TRANSFER(VSREZ(2),1),
     &       TRANSFER(VSREZ(3),1),TRANSFER(VSREZ(4),1),
     &       ' T=',TRANSFER(THI,1),TRANSFER(DSI,1)
          ENDIF
C---- trace delta+matrix at s2 wake stn103 call 3 iters 6-8
          IF(IS.EQ.2 .AND. IBL.EQ.103 .AND.
     &       MRCHDU_COUNT.EQ.3 .AND. ITBL.GE.6
     &       .AND. ITBL.LE.8) THEN
           WRITE(*,'(A,I2,1X,Z8,1X,Z8,1X,Z8,1X,Z8,A,Z8)')
     &      'F_WD103_',ITBL,
     &      TRANSFER(VSREZ(1),1),TRANSFER(VSREZ(2),1),
     &      TRANSFER(VSREZ(3),1),TRANSFER(VSREZ(4),1),
     &      ' S=',TRANSFER(SENS,1)
           DO 9601 JJR=1, 4
            WRITE(*,'(A,I2,A,I1,A,Z8,1X,Z8,1X,Z8,1X,Z8,A,Z8)')
     &       'F_WM103_',ITBL,' r',JJR-1,':',
     &       TRANSFER(VS2(JJR,1),1),
     &       TRANSFER(VS2(JJR,2),1),
     &       TRANSFER(VS2(JJR,3),1),
     &       TRANSFER(VS2(JJR,4),1),
     &       ' |',TRANSFER(VSREZ(JJR),1)
 9601      CONTINUE
          ENDIF
          IF(IS.EQ.2 .AND. (IBL.EQ.5.OR.IBL.EQ.6.OR.IBL.EQ.7) .AND.
     &       MRCHDU_COUNT.EQ.2 .AND. ITBL.LE.5) THEN
           WRITE(*,'(A,I1,A,I2,1X,Z8,1X,Z8,1X,Z8,1X,Z8,1X,Z8)')
     &      'F_D',IBL,'_',ITBL,
     &      TRANSFER(VSREZ(1),1),TRANSFER(VSREZ(2),1),
     &      TRANSFER(VSREZ(3),1),TRANSFER(VSREZ(4),1),
     &      TRANSFER(SENS,1)
           WRITE(*,'(A,I2,1X,Z8,1X,Z8,1X,Z8,1X,Z8)')
     &      'F_HK5',ITBL,
     &      TRANSFER(HK2_T2,1),TRANSFER(HK2_D2,1),
     &      TRANSFER(HK2_U2*U2_UEI,1),TRANSFER(HKREF,1)
           WRITE(*,'(A,I2,1X,Z8,1X,Z8,1X,Z8)')
     &      'F_VSREZ5',ITBL,
     &      TRANSFER(VSREZ(1),1),TRANSFER(VSREZ(2),1),
     &      TRANSFER(VSREZ(3),1)
          ENDIF
C-------- determine max changes and underrelax if necessary
C-------- (added Ue clamp   MD  3 Apr 03)
          DMAX = MAX( ABS(VSREZ(2)/THI),
     &                ABS(VSREZ(3)/DSI),
     &                ABS(VSREZ(4)/UEI)  )
          IF(IBL.GE.ITRAN(IS)) DMAX = MAX(DMAX,ABS(VSREZ(1)/(10.0*CTI)))
C
          RLX = 1.0
          IF(DMAX.GT.0.3) RLX = 0.3/DMAX
          IF(IS.EQ.1 .AND. IBL.EQ.27 .AND. TRACE_OUTER.EQ.8) THEN
           WRITE(0,'(A,I2,5(A,Z8))')
     &      'F_RLX27 it=',ITBL,
     &      ' rlx=',TRANSFER(RLX,1),
     &      ' dm=',TRANSFER(DMAX,1),
     &      ' d0=',TRANSFER(VSREZ(1),1),
     &      ' d1=',TRANSFER(VSREZ(2),1),
     &      ' cti=',TRANSFER(CTI,1)
          ENDIF
          IF(IS.EQ.2 .AND. IBL.EQ.85) THEN
           WRITE(0,'(A,I2,A,I2,A,Z8,A,Z8,A,Z8,A,Z8,A,Z8,A,Z8)')
     &      'F_WK85_PRE c=',MRCHDU_COUNT,' it=',ITBL,
     &      ' T0=',TRANSFER(THI,1),
     &      ' D0=',TRANSFER(DSI,1),
     &      ' U0=',TRANSFER(UEI,1),
     &      ' C0=',TRANSFER(CTI,1),
     &      ' RL=',TRANSFER(RLX,1),
     &      ' DM=',TRANSFER(DMAX,1)
          ENDIF
          IF(IS.EQ.1 .AND. (IBL.EQ.4.OR.IBL.EQ.58)
     &       .AND. ITBL.LE.3) THEN
            WRITE(*,'(A,I2,A,I2,A,Z8)')
     &       'F_DMAX',IBL,' it=',ITBL,' dm=',TRANSFER(DMAX,1)
          ENDIF
          IF(IS.EQ.1 .AND. IBL.EQ.58) THEN
            WRITE(*,'(A,I2,A,Z8,4(1X,Z8))')
     &       'F_DMAX58 it=',ITBL,' dm=',TRANSFER(DMAX,1),
     &       TRANSFER(VSREZ(1),1),TRANSFER(VSREZ(2),1),
     &       TRANSFER(VSREZ(3),1),TRANSFER(VSREZ(4),1)
          ENDIF
C
C-------- update as usual
          IF(IS.EQ.2 .AND. IBL.EQ.58) THEN
           WRITE(0,'(A,I2,A,Z8,A,Z8,A,Z8,A,Z8)')
     &      'F_UPD58 it=',ITBL,
     &      ' D_pre=',TRANSFER(DSI,1),
     &      ' d2=',TRANSFER(VSREZ(3),1),
     &      ' rlx=',TRANSFER(RLX,1),
     &      ' T_pre=',TRANSFER(THI,1)
          ENDIF
          IF(IBL.LT.ITRAN(IS)) AMI = AMI + RLX*VSREZ(1)
          IF(IBL.GE.ITRAN(IS)) CTI = CTI + RLX*VSREZ(1)
          THI = THI + RLX*VSREZ(2)
          DSI = DSI + RLX*VSREZ(3)
          UEI = UEI + RLX*VSREZ(4)
C---- n6h20 trace: COM2 dump at IBL=66 IS=2 MRCHDU 10 iter 1-2
          IF(MRCHDU_COUNT.EQ.10 .AND. IS.EQ.2 .AND. IBL.EQ.66
     &       .AND. (ITBL.EQ.1 .OR. ITBL.EQ.2)) THEN
           WRITE(0,'(A,I2,9(A,Z8))')
     &      'F_MDU10_N66_COM2 it=',ITBL,
     &      ' HK2=',TRANSFER(HK2,1),
     &      ' RT2=',TRANSFER(RT2,1),
     &      ' M2=',TRANSFER(M2,1),
     &      ' H2=',TRANSFER(H2,1),
     &      ' CF2=',TRANSFER(CF2,1),
     &      ' HS2=',TRANSFER(HS2,1),
     &      ' US2=',TRANSFER(US2,1),
     &      ' DI2=',TRANSFER(DI2,1),
     &      ' CQ2=',TRANSFER(CQ2,1)
          ENDIF
C---- n6h20 trace: COM1 dump at IBL=66 IS=2 MRCHDU 10 iter 1-2
          IF(MRCHDU_COUNT.EQ.10 .AND. IS.EQ.2 .AND. IBL.EQ.66
     &       .AND. (ITBL.EQ.1 .OR. ITBL.EQ.2)) THEN
           WRITE(0,'(A,I2,10(A,Z8))')
     &      'F_MDU10_N66_COM1 it=',ITBL,
     &      ' T1=',TRANSFER(T1,1),
     &      ' D1=',TRANSFER(D1,1),
     &      ' U1=',TRANSFER(U1,1),
     &      ' HK1=',TRANSFER(HK1,1),
     &      ' RT1=',TRANSFER(RT1,1),
     &      ' CF1=',TRANSFER(CF1,1),
     &      ' HS1=',TRANSFER(HS1,1),
     &      ' US1=',TRANSFER(US1,1),
     &      ' DI1=',TRANSFER(DI1,1),
     &      ' CQ1=',TRANSFER(CQ1,1)
          ENDIF
C---- n6h20 trace: Newton CTI update at IBL=66 IS=2 MRCHDU 10
          IF(MRCHDU_COUNT.EQ.10 .AND. IS.EQ.2 .AND. IBL.EQ.66) THEN
           WRITE(0,'(A,I2,10(A,Z8))')
     &      'F_MDU10_N66 it=',ITBL,
     &      ' CTI=',TRANSFER(CTI,1),
     &      ' THI=',TRANSFER(THI,1),
     &      ' DSI=',TRANSFER(DSI,1),
     &      ' UEI=',TRANSFER(UEI,1),
     &      ' VSREZ1=',TRANSFER(VSREZ(1),1),
     &      ' VSREZ2=',TRANSFER(VSREZ(2),1),
     &      ' VSREZ3=',TRANSFER(VSREZ(3),1),
     &      ' VSREZ4=',TRANSFER(VSREZ(4),1),
     &      ' SENS=',TRANSFER(SENS,1),
     &      ' RLX=',TRANSFER(RLX,1)
          ENDIF
C---- trace per-iteration at station 25 side 1 init MRCHDU
          IF(IS.EQ.1 .AND. IBL.EQ.27 .AND.
     &       TRACE_OUTER.EQ.8) THEN
           WRITE(0,'(A,I2,A,I2,A,Z8,A,Z8,A,Z8,A,Z8,
     &      A,Z8,A,Z8,A,Z8)')
     &      'F_N27 oi=',TRACE_OUTER,' it=',ITBL,
     &      ' T=',TRANSFER(THI,1),
     &      ' D=',TRANSFER(DSI,1),
     &      ' U=',TRANSFER(UEI,1),
     &      ' C=',TRANSFER(CTI,1),
     &      ' d0=',TRANSFER(VSREZ(1),1),
     &      ' d1=',TRANSFER(VSREZ(2),1),
     &      ' d2=',TRANSFER(VSREZ(3),1)
          ENDIF
          IF(IS.EQ.1 .AND. IBL.EQ.25 .AND. TRACE_OUTER.LE.2) THEN
           WRITE(0,'(A,I2,A,Z8,A,Z8,A,Z8,A,Z8,A,Z8)')
     &      'F_IT25 it=',ITBL,
     &      ' T=',TRANSFER(THI,1),
     &      ' D=',TRANSFER(DSI,1),
     &      ' d1=',TRANSFER(VSREZ(2),1),
     &      ' d2=',TRANSFER(VSREZ(3),1),
     &      ' rlx=',TRANSFER(RLX,1)
          ENDIF
C
          IF(IS.EQ.2 .AND. IBL.EQ.58) THEN
           WRITE(0,'(A,I2,A,Z8,A,Z8)')
     &      'F_UPD58 it=',ITBL,
     &      ' D_add=',TRANSFER(DSI,1),
     &      ' T_add=',TRANSFER(THI,1)
          ENDIF
          IF(IS.EQ.2 .AND. IBL.EQ.3 .AND. MRCHDU_COUNT.EQ.1) THEN
           WRITE(0,'(A,I2,A,Z8,A,Z8,A,Z8)')
     &      'F_MDU3 it=',ITBL,
     &      ' T=',TRANSFER(THI,1),
     &      ' D=',TRANSFER(DSI,1),
     &      ' dm=',TRANSFER(DMAX,1)
          ENDIF
          IF(IS.EQ.2 .AND. (IBL.EQ.93.OR.IBL.EQ.98)
     &       .AND. MRCHDU_COUNT.LE.2) THEN
           WRITE(0,'(A,I3,A,I1,A,I2,4(A,Z8))')
     &      'F_DEL',IBL,
     &      ' mc=',MRCHDU_COUNT,' it=',ITBL,
     &      ' D0=',TRANSFER(VSREZ(1),1),
     &      ' D1=',TRANSFER(VSREZ(2),1),
     &      ' D2=',TRANSFER(VSREZ(3),1),
     &      ' D3=',TRANSFER(VSREZ(4),1)
           WRITE(0,'(A,I2,A,Z8,A,Z8,A,Z8,A,Z8,A,Z8)')
     &      'F_MDU93R it=',ITBL,
     &      ' R0=',TRANSFER(VSREZ(1),1),
     &      ' R1=',TRANSFER(VSREZ(2),1),
     &      ' R2=',TRANSFER(VSREZ(3),1),
     &      ' T=',TRANSFER(THI,1),
     &      ' D=',TRANSFER(DSI,1)
          ENDIF
          IF(IS.EQ.2 .AND. IBL.EQ.104 .AND. MRCHDU_COUNT.EQ.2) THEN
           WRITE(0,'(A,I2,A,Z8,A,Z8,A,Z8,A,Z8)')
     &      'F_MDU104 it=',ITBL,
     &      ' T=',TRANSFER(THI,1),
     &      ' D=',TRANSFER(DSI,1),
     &      ' U=',TRANSFER(UEI,1),
     &      ' M=',TRANSFER(DSI*UEI,1)
          ENDIF
          IF(IS.EQ.2 .AND. IBL.EQ.12 .AND. MRCHDU_COUNT.EQ.2) THEN
           WRITE(0,'(A,I2,A,Z8,A,Z8,A,Z8,A,Z8,A,Z8)')
     &      'F_MDU12 it=',ITBL,
     &      ' T=',TRANSFER(THI,1),
     &      ' D=',TRANSFER(DSI,1),
     &      ' U=',TRANSFER(UEI,1),
     &      ' C=',TRANSFER(CTI,1),
     &      ' dm=',TRANSFER(DMAX,1)
          ENDIF
C-------- eliminate absurd transients
          IF(IBL.GE.ITRAN(IS)) THEN
           CTI = MIN(CTI , 0.30 )
           CTI = MAX(CTI , 0.0000001 )
          ENDIF
C
          IF(IBL.LE.IBLTE(IS)) THEN
            HKLIM = 1.02
          ELSE
            HKLIM = 1.00005
          ENDIF
          MSQ = UEI*UEI*HSTINV / (GM1BL*(1.0 - 0.5*UEI*UEI*HSTINV))
          DSW = DSI - DSWAKI
          CALL DSLIM(DSW,THI,UEI,MSQ,HKLIM)
          DSI = DSW + DSWAKI
          IF(IS.EQ.2 .AND. IBL.EQ.85) THEN
           WRITE(0,'(A,I2,A,I2,A,Z8,A,Z8,A,Z8,A,Z8,A,Z8,A,Z8)')
     &      'F_WK85_POST c=',MRCHDU_COUNT,' it=',ITBL,
     &      ' T=',TRANSFER(THI,1),
     &      ' D=',TRANSFER(DSI,1),
     &      ' U=',TRANSFER(UEI,1),
     &      ' C=',TRANSFER(CTI,1),
     &      ' DSW=',TRANSFER(DSW,1),
     &      ' MSQ=',TRANSFER(MSQ,1)
          ENDIF
C
          IF(IS.EQ.2 .AND. IBL.EQ.58) THEN
           WRITE(0,'(A,I2,A,Z8,A,Z8)')
     &      'F_UPD58 it=',ITBL,
     &      ' D_lim=',TRANSFER(DSI,1),
     &      ' T_lim=',TRANSFER(THI,1)
          ENDIF
          IF(IS.EQ.2 .AND. IBL.EQ.5) THEN
           WRITE(0,'(A,I2,A,Z8,A,Z8,A,Z8,A,Z8,A,Z8,
     &      A,Z8,A,Z8,A,E10.4)')
     &      'F_MDU5 it=',ITBL,
     &      ' T=',TRANSFER(THI,1),
     &      ' D=',TRANSFER(DSI,1),
     &      ' R2=',TRANSFER(VSREZ(2),1),
     &      ' R3=',TRANSFER(VSREZ(3),1),
     &      ' R4=',TRANSFER(VSREZ(4),1),
     &      ' RX=',TRANSFER(RLX,1),
     &      ' U=',TRANSFER(UEI,1),
     &      ' DM=',DMAX
          ENDIF
          IF(IS.EQ.2 .AND. IBL.GE.82 .AND. IBL.LE.84
     &       .AND. MRCHDU_COUNT.EQ.1) THEN
           WRITE(0,'(A,I3,A,I2,A,Z8,A,Z8,A,Z8,A,E10.3)')
     &      'F_WK83 I=',IBL,' it=',ITBL,
     &      ' T=',TRANSFER(THI,1),
     &      ' D=',TRANSFER(DSI,1),
     &      ' U=',TRANSFER(UEI,1),
     &      ' DM=',DMAX
          ENDIF
          IF(IS.EQ.2 .AND. IBL.GE.91 .AND. IBL.LE.94
     &       .AND. MRCHDU_COUNT.EQ.19) THEN
           WRITE(0,'(A,I3,A,I2,A,Z8,A,Z8,A,Z8,A,Z8)')
     &      'F_MDUIT mc=19 ibl=',IBL,' it=',ITBL,
     &      ' DMAX=',TRANSFER(DMAX,1),
     &      ' THI=',TRANSFER(THI,1),
     &      ' DSI=',TRANSFER(DSI,1),
     &      ' UEI=',TRANSFER(UEI,1)
          ENDIF
          IF(DMAX.LE.DEPS) GO TO 110
C
  100   CONTINUE
C
        WRITE(*,1350) IBL, IS, DMAX
 1350   FORMAT(' MRCHDU: Convergence failed at',I4,'  side',I2,
     &         '    Res =', E12.4)
C
C------ the current unconverged solution might still be reasonable...
CCC        IF(DMAX .LE. 0.1) GO TO 110
        IF(IS.EQ.2 .AND. IBL.GE.91 .AND. IBL.LE.94
     &     .AND. MRCHDU_COUNT.EQ.19) THEN
         WRITE(0,'(A,I3,A,Z8)')
     &    'F_NEWTON_END mc=19 ibl=',IBL,' dmax=',TRANSFER(DMAX,1)
        ENDIF
        IF(DMAX .LE. 0.1) GO TO 109
C
C------- the current solution is garbage --> extrapolate values instead
         IF(IBL.GT.3) THEN
          IF(IBL.LE.IBLTE(IS)) THEN
           THI = THET(IBM,IS) * (XSSI(IBL,IS)/XSSI(IBM,IS))**0.5
           DSI = DSTR(IBM,IS) * (XSSI(IBL,IS)/XSSI(IBM,IS))**0.5
           UEI = UEDG(IBM,IS)
          ELSE IF(IBL.EQ.IBLTE(IS)+1) THEN
           CTI = CTE
           THI = TTE
           DSI = DTE
           UEI = UEDG(IBM,IS)
          ELSE
           THI = THET(IBM,IS)
           RATLEN = (XSSI(IBL,IS)-XSSI(IBM,IS)) / (10.0*DSTR(IBM,IS))
           DSI = (DSTR(IBM,IS) + THI*RATLEN) / (1.0 + RATLEN)
           UEI = UEDG(IBM,IS)
          ENDIF
          IF(IBL.EQ.ITRAN(IS)) CTI = 0.05
          IF(IBL.GT.ITRAN(IS)) CTI = CTAU(IBM,IS)
         ENDIF
C
 109     CALL BLPRV(XSI,AMI,CTI,THI,DSI,DSWAKI,UEI)
         CALL BLKIN
         IF(IS.EQ.2 .AND. IBL.EQ.91 .AND. MRCHDU_COUNT.EQ.3) THEN
          WRITE(0,'(A,5(A,Z8))')
     &     'F_109A91',
     &     ' THI=',TRANSFER(THI,1),
     &     ' DSI=',TRANSFER(DSI,1),
     &     ' UEI=',TRANSFER(UEI,1),
     &     ' HK2=',TRANSFER(HK2,1),
     &     ' H2=',TRANSFER(H2,1)
         ENDIF
C
C------- check for transition and set appropriate flags and things
         IF((.NOT.SIMI) .AND. (.NOT.TURB)) THEN
          CALL TRCHEK
          AMI = AMPL2
          IF(     TRAN) ITRAN(IS) = IBL
          IF(.NOT.TRAN) ITRAN(IS) = IBL+2
          IF(IS.EQ.1 .AND. IBL.GE.3 .AND. IBL.LE.11
     &       .AND. MRCHDU_COUNT.EQ.20) THEN
           WRITE(0,'(A,I3,A,L1,A,Z8,A,Z8,A,Z8,A,I3)')
     &      'F_PFCM_POST mc=20 ibl=',IBL,' tran=',TRAN,
     &      ' A1=',TRANSFER(AMPL1,1),
     &      ' A2=',TRANSFER(AMI,1),
     &      ' XT=',TRANSFER(XT,1),
     &      ' ITRAN->',ITRAN(IS)
          ENDIF
         ENDIF
C
C------- set all other extrapolated values for current station
         IF(IBL.LT.ITRAN(IS)) CALL BLVAR(1)
         IF(IBL.GE.ITRAN(IS)) CALL BLVAR(2)
         IF(WAKE) CALL BLVAR(3)
         IF(IS.EQ.2 .AND. IBL.EQ.91 .AND. MRCHDU_COUNT.EQ.3) THEN
          WRITE(0,'(A,4(A,Z8))')
     &     'F_109B91',
     &     ' HK2=',TRANSFER(HK2,1),
     &     ' HS2=',TRANSFER(HS2,1),
     &     ' DI2=',TRANSFER(DI2,1),
     &     ' US2=',TRANSFER(US2,1)
         ENDIF
C
         IF(IBL.LT.ITRAN(IS)) CALL BLMID(1)
         IF(IBL.GE.ITRAN(IS)) CALL BLMID(2)
         IF(WAKE) CALL BLMID(3)
C
C------ pick up here after the Newton iterations
  110   CONTINUE
C
        SENS = SENNEW
C
C------ store primary variables
        IF(IBL.LT.ITRAN(IS)) CTAU(IBL,IS) = AMI
        IF(IBL.GE.ITRAN(IS)) CTAU(IBL,IS) = CTI
        THET(IBL,IS) = THI
        DSTR(IBL,IS) = DSI
        UEDG(IBL,IS) = UEI
        MASS(IBL,IS) = DSI*UEI
C---- trace MRCHDU store at key stations
        IF(IS.EQ.1 .AND. (IBL.EQ.24 .OR. IBL.EQ.25)) THEN
         WRITE(0,'(A,I2,A,I4,A,Z8,A,Z8)')
     &    'F_T2425 C=',MRCHDU_COUNT,' I=',IBL,
     &    ' T=',TRANSFER(THI,1),
     &    ' C=',TRANSFER(CTAU(IBL,IS),1)
        ENDIF
        IF((MRCHDU_COUNT.GE.2 .AND. MRCHDU_COUNT.LE.3)
     &     .OR. MRCHDU_COUNT.EQ.14) THEN
         WRITE(0,'(A,I1,A,I2,A,I4,A,Z8,A,Z8,A,Z8)')
     &    'F_MDU S=',IS,' C=',MRCHDU_COUNT,' I=',IBL,
     &    ' T=',TRANSFER(THI,1),
     &    ' D=',TRANSFER(DSI,1),
     &    ' U=',TRANSFER(UEI,1)
        ENDIF
        IF(MRCHDU_COUNT.EQ.10 .AND. IS.EQ.2 .AND. IBL.GE.65 .AND. IBL.LE.73) THEN
         WRITE(0,'(A,I3,A,Z8,A,Z8,A,Z8,A,Z8,A,Z8,A,Z8,A,Z8,A,Z8)')
     &    'F_MDU10_STORE ibl=',IBL,
     &    ' T=',TRANSFER(THET(IBL,IS),1),
     &    ' D=',TRANSFER(DSTR(IBL,IS),1),
     &    ' C=',TRANSFER(CTAU(IBL,IS),1),
     &    ' HK2=',TRANSFER(HK2,1),
     &    ' RT2=',TRANSFER(RT2,1),
     &    ' HS2=',TRANSFER(HS2,1),
     &    ' US2=',TRANSFER(US2,1),
     &    ' CQ2=',TRANSFER(CQ2,1)
        ENDIF
C---- n6h20 trace: per-station MRCHDU output at call 10 side 2 wake
        IF(MRCHDU_COUNT.EQ.10 .AND. IS.EQ.2 .AND. IBL.GE.65) THEN
         WRITE(0,'(A,I3,A,Z8,A,Z8,A,I3,A,Z8,A,Z8)')
     &    'F_MDU10S2 ibl=',IBL,
     &    ' T=',TRANSFER(THI,1),
     &    ' D=',TRANSFER(DSI,1),
     &    ' iw=',IBL-IBLTE(IS),
     &    ' WG=',TRANSFER(WGAP(MAX(IBL-IBLTE(IS),1)),1),
     &    ' WGm1=',TRANSFER(WGAP(MAX(IBL-IBLTE(IS)-1,1)),1)
        ENDIF
        IF(IS.EQ.2 .AND. (IBL.EQ.57 .OR. IBL.EQ.58)) THEN
         WRITE(0,'(A,I3,A,Z8,A,Z8,A,Z8)')
     &    'F_UPD58 ACPT IBL=',IBL,
     &    ' D=',TRANSFER(DSI,1),
     &    ' T=',TRANSFER(THI,1),
     &    ' U=',TRANSFER(UEI,1)
        ENDIF
C---- GDB parity: dump MRCHDU wake store
        IF(WAKE) THEN
         WRITE(0,'(A,I2,A,I3,A,Z8,A,Z8,A,Z8,A,Z8)')
     &    'F_MRCHDU_WK IS=',IS,' IBL=',IBL,
     &    ' THET=',TRANSFER(THI,1),
     &    ' DSTR=',TRANSFER(DSI,1),
     &    ' UEDG=',TRANSFER(UEI,1),
     &    ' MASS=',TRANSFER(MASS(IBL,IS),1)
        ENDIF
        CALL TRACE_LEGACY_SEED_FINAL('MRCHDU', IS, IBL,
     &       WAKE, TURB, TRAN, DMAX.LE.DEPS,
     &       UEI, THI, DSI, CTI, AMI, ITRAN(IS), MASS(IBL,IS))
        TAU(IBL,IS)  = 0.5*R2*U2*U2*CF2
        DIS(IBL,IS)  =     R2*U2*U2*U2*DI2*HS2*0.5
        CTQ(IBL,IS)  = CQ2
        DELT(IBL,IS) = DE2
        TSTR(IBL,IS) = HS2*T2
C---- GDB: MRCHDU per-station trace for all calls
        IF(MRCHDU_COUNT.GE.2.AND.MRCHDU_COUNT.LE.4) THEN
         WRITE(*,'(A,I1,A,I1,A,I3,A,Z8,A,Z8,A,Z8)')
     &    'F_MDU S=',IS,' C=',MRCHDU_COUNT,' I=',IBL,
     &    ' T=',TRANSFER(THI,1),
     &    ' D=',TRANSFER(DSI,1),
     &    ' U=',TRANSFER(UEI,1)
        ENDIF
C
C------ set "1" variables to "2" variables for next streamwise station
        IF(IS.EQ.2 .AND. IBL.EQ.91 .AND. MRCHDU_COUNT.EQ.3) THEN
         WRITE(0,'(A,8(A,Z8))')
     &    'F_PREBPK91',
     &    ' CF=',TRANSFER(CF2,1),
     &    ' DI=',TRANSFER(DI2,1),
     &    ' HS=',TRANSFER(HS2,1),
     &    ' US=',TRANSFER(US2,1),
     &    ' CQ=',TRANSFER(CQ2,1),
     &    ' T2=',TRANSFER(T2,1),
     &    ' D2=',TRANSFER(D2,1),
     &    ' U2=',TRANSFER(U2,1)
        ENDIF
        CALL BLPRV(XSI,AMI,CTI,THI,DSI,DSWAKI,UEI)
        CALL BLKIN
        IF(IS.EQ.2 .AND. IBL.EQ.91 .AND. MRCHDU_COUNT.EQ.3) THEN
         WRITE(0,'(A,8(A,Z8))')
     &    'F_POSTBPK91',
     &    ' CF=',TRANSFER(CF2,1),
     &    ' DI=',TRANSFER(DI2,1),
     &    ' HS=',TRANSFER(HS2,1),
     &    ' US=',TRANSFER(US2,1),
     &    ' CQ=',TRANSFER(CQ2,1),
     &    ' T2=',TRANSFER(T2,1),
     &    ' D2=',TRANSFER(D2,1),
     &    ' U2=',TRANSFER(U2,1)
        ENDIF
        DO 310 ICOM=1, NCOM
          COM1(ICOM) = COM2(ICOM)
  310   CONTINUE
C
C
C------ turbulent intervals will follow transition interval or TE
        IF(TRAN .OR. IBL.EQ.IBLTE(IS)) THEN
         TURB = .TRUE.
C
C------- save transition location
         TFORCE(IS) = TRFORC
         XSSITR(IS) = XT
        ENDIF
C
        TRAN = .FALSE.
C
 1000 CONTINUE
C
 2000 CONTINUE
C
      RETURN
      END
  
 
      SUBROUTINE XIFSET(IS)
C-----------------------------------------------------
C     Sets forced-transition BL coordinate locations.
C-----------------------------------------------------
      INCLUDE 'XFOIL.INC'
      INCLUDE 'XBL.INC'
C
      IF(XSTRIP(IS).GE.1.0) THEN
       XIFORC = XSSI(IBLTE(IS),IS)
       RETURN
      ENDIF
C
      CHX = XTE - XLE
      CHY = YTE - YLE
      CHSQ = CHX**2 + CHY**2
C
C---- calculate chord-based x/c, y/c
      DO 10 I=1, N
        W1(I) = ((X(I)-XLE)*CHX + (Y(I)-YLE)*CHY) / CHSQ
        W2(I) = ((Y(I)-YLE)*CHX - (X(I)-XLE)*CHY) / CHSQ
 10   CONTINUE
C
      CALL SPLIND(W1,W3,S,N,-999.0,-999.0)
      CALL SPLIND(W2,W4,S,N,-999.0,-999.0)
C
      IF(IS.EQ.1) THEN
C
C----- set approximate arc length of forced transition point for SINVRT
       STR = SLE + (S(1)-SLE)*XSTRIP(IS)
C
C----- calculate actual arc length
       CALL SINVRT(STR,XSTRIP(IS),W1,W3,S,N)
C
C----- set BL coordinate value
       XIFORC = MIN( (SST - STR) , XSSI(IBLTE(IS),IS) )
C
      ELSE
C----- same for bottom side
C
       STR = SLE + (S(N)-SLE)*XSTRIP(IS)
       CALL SINVRT(STR,XSTRIP(IS),W1,W3,S,N)
       XIFORC = MIN( (STR - SST) , XSSI(IBLTE(IS),IS) )
C
      ENDIF
C
      IF(XIFORC .LT. 0.0) THEN
       WRITE(*,1000) IS
 1000  FORMAT(/' ***  Stagnation point is past trip on side',I2,'  ***')
       XIFORC = XSSI(IBLTE(IS),IS)
      ENDIF
C
      RETURN
      END




      SUBROUTINE UPDATE
C------------------------------------------------------------------
C      Adds on Newton deltas to boundary layer variables.
C      Checks for excessive changes and underrelaxes if necessary.
C      Calculates max and rms changes.
C      Also calculates the change in the global variable "AC".
C        If LALFA=.TRUE. , "AC" is CL
C        If LALFA=.FALSE., "AC" is alpha
C------------------------------------------------------------------
      INCLUDE 'XFOIL.INC'
      REAL UNEW(IVX,2), U_AC(IVX,2)
      REAL QNEW(IQX),   Q_AC(IQX)
      EQUIVALENCE (VA(1,1,1), UNEW(1,1)) ,
     &            (VB(1,1,1), QNEW(1)  )
      EQUIVALENCE (VA(1,1,IVX), U_AC(1,1)) ,
     &            (VB(1,1,IVX), Q_AC(1)  )
      REAL MSQ
      INTEGER UPDATE_COUNT
      INTEGER HASH_T, HASH_D, HASH_U, HASH_C
      INTEGER ISH, IBLH, IXX
      SAVE UPDATE_COUNT
      DATA UPDATE_COUNT /0/
C
C---- max allowable alpha changes per iteration
      DALMAX =  0.5*DTOR
      DALMIN = -0.5*DTOR
C
C---- max allowable CL change per iteration
      DCLMAX =  0.5
      DCLMIN = -0.5
      IF(MATYP.NE.1) DCLMIN = MAX(-0.5 , -0.9*CL)
      UPDATE_COUNT = UPDATE_COUNT + 1
C---- n6h20 trace: hash BL state at top of UPDATE for iter 8-10
      IF(UPDATE_COUNT.EQ.2 .OR. UPDATE_COUNT.EQ.3 .OR. UPDATE_COUNT.EQ.9 .OR. UPDATE_COUNT.EQ.10) THEN
        DO ISH=1, 2
          DO IBLH=2, MIN(NBL(ISH), 4)
            WRITE(0,'(A,I2,A,I1,A,I3,A,Z8,A,Z8)')
     &       'F_BLDUMP it=',UPDATE_COUNT,' s=',ISH,' i=',IBLH,
     &       ' T=',TRANSFER(THET(IBLH,ISH),1),
     &       ' D=',TRANSFER(DSTR(IBLH,ISH),1)
          ENDDO
          DO IBLH=NBL(ISH)-2, NBL(ISH)
            WRITE(0,'(A,I2,A,I1,A,I3,A,Z8,A,Z8)')
     &       'F_BLDUMP it=',UPDATE_COUNT,' s=',ISH,' i=',IBLH,
     &       ' T=',TRANSFER(THET(IBLH,ISH),1),
     &       ' D=',TRANSFER(DSTR(IBLH,ISH),1)
          ENDDO
        ENDDO
      ENDIF
      IF(UPDATE_COUNT.GE.7 .AND. UPDATE_COUNT.LE.11) THEN
        HASH_T = 0
        HASH_D = 0
        HASH_U = 0
        HASH_C = 0
        DO ISH=1, 2
          DO IBLH=2, NBL(ISH)
            HASH_T = IEOR(HASH_T, TRANSFER(THET(IBLH,ISH),1))
            HASH_D = IEOR(HASH_D, TRANSFER(DSTR(IBLH,ISH),1))
            HASH_U = IEOR(HASH_U, TRANSFER(UEDG(IBLH,ISH),1))
            HASH_C = IEOR(HASH_C, TRANSFER(CTAU(IBLH,ISH),1))
          ENDDO
        ENDDO
        WRITE(0,'(A,I2,A,I3,A,I3,A,Z8,A,Z8,A,Z8,A,Z8)')
     &   'F_BLHASH it=',UPDATE_COUNT,
     &   ' NBL1=',NBL(1),' NBL2=',NBL(2),
     &   ' hT=',HASH_T,' hD=',HASH_D,' hU=',HASH_U,' hC=',HASH_C
      ENDIF
C
      HSTINV = GAMM1*(MINF/QINF)**2 / (1.0 + 0.5*GAMM1*MINF**2)
C
C---- calculate new Ue distribution assuming no under-relaxation
C-    also set the sensitivity of Ue wrt to alpha or Re
      DO 1 IS=1, 2
        DO 10 IBL=2, NBL(IS)
          I = IPAN(IBL,IS)
C
          DUI    = 0.
          DUI_AC = 0.
          DO 100 JS=1, 2
            DO 1000 JBL=2, NBL(JS)
              J  = IPAN(JBL,JS)
              JV = ISYS(JBL,JS)
              UE_M = -VTI(IBL,IS)*VTI(JBL,JS)*DIJ(I,J)
              DUI    = DUI    + UE_M*(MASS(JBL,JS)+VDEL(3,1,JV))
              DUI_AC = DUI_AC + UE_M*(            -VDEL(3,2,JV))
C---- GDB: per-term DUI at TE s1 (first 3 + last 2)
              IF(IBL.EQ.IBLTE(IS) .AND. IS.EQ.1
     &           .AND. (JV.LE.3 .OR. JV.GE.NSYS-1)) THEN
               WRITE(0,'(A,I3,A,I3,A,I2,A,Z8,A,Z8)')
     &          'F_URELX jv=',JV,' jbl=',JBL,' js=',JS,
     &          ' dM=',TRANSFER(MASS(JBL,JS)+VDEL(3,1,JV),1),
     &          ' DUI=',TRANSFER(DUI,1)
               IF(JV.EQ.1) THEN
                WRITE(0,'(A,Z8,A,Z8)')
     &           'F_URELX_D MASS=',TRANSFER(MASS(JBL,JS),1),
     &           ' VDEL=',TRANSFER(VDEL(3,1,JV),1)
               ENDIF
              ENDIF
 1000       CONTINUE
  100     CONTINUE
C
C-------- UINV depends on "AC" only if "AC" is alpha
          IF(LALFA) THEN
           UINV_AC = 0.
          ELSE
           UINV_AC = UINV_A(IBL,IS)
          ENDIF
C
          UNEW(IBL,IS) = UINV(IBL,IS) + DUI
          U_AC(IBL,IS) = UINV_AC      + DUI_AC
          JVX = ISYS(IBL,IS)
          WRITE(0,'(A,I3,A,I1,A,I4,A,Z8.8,A,Z8.8,A,Z8.8,A,Z8.8)')
     &      'F_VDEL3 iv=',JVX,' s=',IS,' i=',IBL,
     &      ' CL=',TRANSFER(CL,1),
     &      ' V30=',TRANSFER(VDEL(3,1,JVX),1),
     &      ' MASS=',TRANSFER(MASS(IBL,IS),1),
     &      ' DUI=',TRANSFER(DUI,1)
C---- GDB: dump UPDATE DUI at station 2 side 1
          IF(IS.EQ.1 .AND. IBL.EQ.2) THEN
           WRITE(0,780) TRANSFER(DUI,1),
     &      TRANSFER(UINV(IBL,IS),1),
     &      TRANSFER(UNEW(IBL,IS),1)
 780       FORMAT('F_UPD2 ',3(1X,Z8.8))
          ENDIF
C---- GDB: dump URELAX at TE stations
          IF(IBL.EQ.IBLTE(IS)) THEN
           WRITE(0,'(A,I2,A,I3,A,Z8,A,Z8,A,Z8,A,I4)')
     &      'F_URELAX IS=',IS,' IBL=',IBL,
     &      ' DUI=',TRANSFER(DUI,1),
     &      ' UINV=',TRANSFER(UINV(IBL,IS),1),
     &      ' UNEW=',TRANSFER(UNEW(IBL,IS),1),
     &      ' I=',IPAN(IBL,IS)
          ENDIF
C
   10   CONTINUE
    1 CONTINUE
C
C---- set new Qtan from new Ue with appropriate sign change
      DO 2 IS=1, 2
        DO 20 IBL=2, IBLTE(IS)
          I = IPAN(IBL,IS)
          QNEW(I) = VTI(IBL,IS)*UNEW(IBL,IS)
          Q_AC(I) = VTI(IBL,IS)*U_AC(IBL,IS)
   20   CONTINUE
    2 CONTINUE
C---- dump qNew and UNEW (every iter; disambiguate by CL which gets printed after)
      DO 2775 IS=1,2
      DO 2776 IBL=2,IBLTE(IS)
        IPI = IPAN(IBL,IS)
        WRITE(0,'(A,I1,A,I4,A,I4,A,Z8.8,A,Z8.8,A,Z8.8,A,Z8.8)')
     &    'F_QNEW s=',IS,' i=',IBL,' p=',IPI,
     &    ' CL=',TRANSFER(CL,1),
     &    ' UN=',TRANSFER(UNEW(IBL,IS),1),
     &    ' QN=',TRANSFER(QNEW(IPI),1),
     &    ' UINV=',TRANSFER(UINV(IBL,IS),1)
 2776 CONTINUE
 2775 CONTINUE
C
C---- calculate new CL from this new Qtan
      SA = SIN(ALFA)
      CA = COS(ALFA)
C
      BETA = SQRT(1.0 - MINF**2)
      BETA_MSQ = -0.5/BETA
C
      BFAC     = 0.5*MINF**2 / (1.0 + BETA)
      BFAC_MSQ = 0.5         / (1.0 + BETA)
     &         - BFAC        / (1.0 + BETA) * BETA_MSQ
C
      CLNEW = 0.
      CL_A  = 0.
      CL_MS = 0.
      CL_AC = 0.
C
      I = 1
      CGINC = 1.0 - (QNEW(I)/QINF)**2
      CPG1  = CGINC / (BETA + BFAC*CGINC)
      CPG1_MS = -CPG1/(BETA + BFAC*CGINC)*(BETA_MSQ + BFAC_MSQ*CGINC)
C
      CPI_Q = -2.0*QNEW(I)/QINF**2
      CPC_CPI = (1.0 - BFAC*CPG1)/ (BETA + BFAC*CGINC)
      CPG1_AC = CPC_CPI*CPI_Q*Q_AC(I)
C
      DO 3 I=1, N
        IP = I+1
        IF(I.EQ.N) IP = 1
C
        CGINC = 1.0 - (QNEW(IP)/QINF)**2
        CPG2  = CGINC / (BETA + BFAC*CGINC)
        CPG2_MS = -CPG2/(BETA + BFAC*CGINC)*(BETA_MSQ + BFAC_MSQ*CGINC)
C
        CPI_Q = -2.0*QNEW(IP)/QINF**2
        CPC_CPI = (1.0 - BFAC*CPG2)/ (BETA + BFAC*CGINC)
        CPG2_AC = CPC_CPI*CPI_Q*Q_AC(IP)
C
        DX   =  (X(IP) - X(I))*CA + (Y(IP) - Y(I))*SA
        DX_A = -(X(IP) - X(I))*SA + (Y(IP) - Y(I))*CA
C
        AG    = 0.5*(CPG2    + CPG1   )
        AG_MS = 0.5*(CPG2_MS + CPG1_MS)
        AG_AC = 0.5*(CPG2_AC + CPG1_AC)
C
        CLNEW = CLNEW + DX  *AG
        CL_A  = CL_A  + DX_A*AG
        CL_MS = CL_MS + DX  *AG_MS
        CL_AC = CL_AC + DX  *AG_AC
C (F_CL_STEP trace removed - was in wrong scope)
C
        CPG1    = CPG2
        CPG1_MS = CPG2_MS
        CPG1_AC = CPG2_AC
    3 CONTINUE
C
C---- initialize under-relaxation factor
      RLX = 1.0
C
      IF(LALFA) THEN
C===== alpha is prescribed: AC is CL
C
C----- set change in Re to account for CL changing, since Re = Re(CL)
       DAC = (CLNEW - CL) / (1.0 - CL_AC - CL_MS*2.0*MINF*MINF_CL)
       WRITE(0,'(A,A,Z8,A,Z8,A,Z8,A,Z8,A,Z8)')
     &  'F_DAC_DBG',
     &  ' CL=',TRANSFER(CL,1),
     &  ' CLNEW=',TRANSFER(CLNEW,1),
     &  ' CLAC=',TRANSFER(CL_AC,1),
     &  ' CLMS=',TRANSFER(CL_MS,1),
     &  ' DAC=',TRANSFER(DAC,1)
C
C----- set under-relaxation factor if Re change is too large
       IF(RLX*DAC .GT. DCLMAX) RLX = DCLMAX/DAC
       IF(RLX*DAC .LT. DCLMIN) RLX = DCLMIN/DAC
C
      ELSE
C===== CL is prescribed: AC is alpha
C
C----- set change in alpha to drive CL to prescribed value
       DAC = (CLNEW - CLSPEC) / (0.0 - CL_AC - CL_A)
C
C----- set under-relaxation factor if alpha change is too large
       IF(RLX*DAC .GT. DALMAX) RLX = DALMAX/DAC
       IF(RLX*DAC .LT. DALMIN) RLX = DALMIN/DAC
C
      ENDIF
C
      RMSBL = 0.
      RMXBL = 0.
C
      DHI = 1.5
      DLO = -.5
C
C---- calculate changes in BL variables and under-relaxation if needed
      DO 4 IS=1, 2
        DO 40 IBL=2, NBL(IS)
          IV = ISYS(IBL,IS)
C


C-------- set changes without underrelaxation
C---- trace post-BLSOLV VDEL at station 31 (IV=31) during 14th UPDATE
          IF(IV.EQ.31 .AND. UPDATE_COUNT.EQ.14) THEN
           WRITE(0,'(A,I2,A,Z8,A,Z8,A,Z8,A,Z8,A,Z8)')
     &      'F_SOL30 c=',UPDATE_COUNT,
     &      ' v0=',TRANSFER(VDEL(1,1,IV),1),
     &      ' v1=',TRANSFER(VDEL(2,1,IV),1),
     &      ' v2=',TRANSFER(VDEL(3,1,IV),1),
     &      ' v0c2=',TRANSFER(VDEL(1,2,IV),1),
     &      ' dac=',TRANSFER(DAC,1)
          ENDIF
          DCTAU = VDEL(1,1,IV) - DAC*VDEL(1,2,IV)
          DTHET = VDEL(2,1,IV) - DAC*VDEL(2,2,IV)
          DMASS = VDEL(3,1,IV) - DAC*VDEL(3,2,IV)
          DUEDG = UNEW(IBL,IS) + DAC*U_AC(IBL,IS)  -  UEDG(IBL,IS)
          DDSTR = (DMASS - DSTR(IBL,IS)*DUEDG)/UEDG(IBL,IS)
          IF(UPDATE_COUNT.EQ.9 .AND. IS.EQ.2 .AND. IBL.GE.85) THEN
           WRITE(0,'(A,I2,A,I1,A,I3,A,Z8,A,Z8,A,Z8,A,Z8,
     &      A,Z8,A,Z8,A,Z8,A,Z8,A,Z8,A,Z8,A,Z8,
     &      A,Z8,A,Z8,A,Z8,A,Z8)')
     &      'F_UPD_SCAN U=',UPDATE_COUNT,' S=',IS,' I=',IBL,
     &      ' V10=',TRANSFER(VDEL(1,1,IV),1),
     &      ' V11=',TRANSFER(VDEL(1,2,IV),1),
     &      ' V20=',TRANSFER(VDEL(2,1,IV),1),
     &      ' V21=',TRANSFER(VDEL(2,2,IV),1),
     &      ' V30=',TRANSFER(VDEL(3,1,IV),1),
     &      ' V31=',TRANSFER(VDEL(3,2,IV),1),
     &      ' DAC=',TRANSFER(DAC,1),
     &      ' DC=',TRANSFER(DCTAU,1),
     &      ' DT=',TRANSFER(DTHET,1),
     &      ' DM=',TRANSFER(DMASS,1),
     &      ' DU=',TRANSFER(DUEDG,1),
     &      ' DD=',TRANSFER(DDSTR,1)
          ENDIF
C
C-------- normalize changes
          IF(IBL.LT.ITRAN(IS)) DN1 = DCTAU / 10.0
          IF(IBL.GE.ITRAN(IS)) DN1 = DCTAU / CTAU(IBL,IS)
          DN2 = DTHET / THET(IBL,IS)
          DN3 = DDSTR / DSTR(IBL,IS)
          DN4 = ABS(DUEDG)/0.25
C
C-------- accumulate for rms change
          RMSBL = RMSBL + DN1**2 + DN2**2 + DN3**2 + DN4**2
C          
C-------- see if Ctau needs underrelaxation
          RDN1 = RLX*DN1
          IF(ABS(DN1) .GT. ABS(RMXBL)) THEN
           RMXBL = DN1
           IF(IBL.LT.ITRAN(IS)) VMXBL = 'n'
           IF(IBL.GE.ITRAN(IS)) VMXBL = 'C'
           IMXBL = IBL
           ISMXBL = IS
          ENDIF
          IF(RDN1 .GT. DHI) RLX = DHI/DN1
          IF(RDN1 .LT. DLO) RLX = DLO/DN1
C
C-------- see if Theta needs underrelaxation
          RDN2 = RLX*DN2
          IF(ABS(DN2) .GT. ABS(RMXBL)) THEN
           RMXBL = DN2
           VMXBL = 'T'
           IMXBL = IBL
           ISMXBL = IS
          ENDIF
          IF(RDN2 .GT. DHI) RLX = DHI/DN2
          IF(RDN2 .LT. DLO) RLX = DLO/DN2
C
C---- trace DN3 at station 31 IS=1 at iteration 14
          IF(UPDATE_COUNT.EQ.14 .AND. IS.EQ.1 .AND. IBL.EQ.31) THEN
           WRITE(0,'(A,Z8,A,Z8,A,Z8,A,Z8,A,Z8,A,Z8,
     &      A,Z8,A,Z8,A,Z8,A,Z8,A,Z8)')
     &      'F_DN3_31 DN3=',TRANSFER(DN3,1),
     &      ' RLX=',TRANSFER(RLX,1),
     &      ' DDSTR=',TRANSFER(DDSTR,1),
     &      ' DSTR=',TRANSFER(DSTR(IBL,IS),1),
     &      ' DUEDG=',TRANSFER(DUEDG,1),
     &      ' DMASS=',TRANSFER(DMASS,1),
     &      ' UEDG=',TRANSFER(UEDG(IBL,IS),1),
     &      ' DTHET=',TRANSFER(DTHET,1),
     &      ' THET=',TRANSFER(THET(IBL,IS),1),
     &      ' DN2=',TRANSFER(DN2,1),
     &      ' DAC=',TRANSFER(DAC,1)
          ENDIF
C-------- see if Dstar needs underrelaxation
          RDN3 = RLX*DN3
          IF(ABS(DN3) .GT. ABS(RMXBL)) THEN
           RMXBL = DN3
           VMXBL = 'D'
           IMXBL = IBL
           ISMXBL = IS
          ENDIF
          IF(RDN3 .GT. DHI) RLX = DHI/DN3
          IF(RDN3 .LT. DLO) RLX = DLO/DN3
C
C-------- see if Ue needs underrelaxation
          RDN4 = RLX*DN4
          IF(ABS(DN4) .GT. ABS(RMXBL)) THEN
           RMXBL = DUEDG
           VMXBL = 'U'
           IMXBL = IBL
           ISMXBL = IS
          ENDIF
          IF(RDN4 .GT. DHI) RLX = DHI/DN4
          IF(RDN4 .LT. DLO) RLX = DLO/DN4
C
   40   CONTINUE
    4 CONTINUE
C
C---- trace RLX-limiting station at iteration 14
      IF(UPDATE_COUNT.EQ.14) THEN
       WRITE(0,'(A,Z8,A,I3,A,I1,A,A1)')
     &  'F_RLX14 rlx=',TRANSFER(RLX,1),
     &  ' IBL=',IMXBL,' IS=',ISMXBL,' V=',VMXBL
      ENDIF
C---- n6h20 trace: RLX limiting station at iter 1
      IF(UPDATE_COUNT.EQ.1) THEN
       WRITE(0,'(A,Z8,A,I3,A,I1,A,A1,A,Z8)')
     &  'F_RLX1 rlx=',TRANSFER(RLX,1),
     &  ' IBL=',IMXBL,' IS=',ISMXBL,' V=',VMXBL,
     &  ' RMXBL=',TRANSFER(RMXBL,1)
      ENDIF
C---- set true rms change
      RMSBL = SQRT( RMSBL / (4.0*FLOAT( NBL(1)+NBL(2) )) )
C
C
      IF(LALFA) THEN
C----- dump CL before update (matches C_CL_UPD)
       WRITE(0,'(A,Z8,A,Z8,A,Z8,A,Z8)')
     & 'F_CL_UPD cl_pre=',TRANSFER(CL,1),
     & ' rlx_dac=',TRANSFER(RLX*DAC,1),
     & ' rlx=',TRANSFER(RLX,1),
     & ' dac=',TRANSFER(DAC,1)
C----- set underrelaxed change in Reynolds number from change in lift
       CL = CL + RLX*DAC
      ELSE
C----- set underrelaxed change in alpha
       ALFA = ALFA + RLX*DAC
       ADEG = ALFA/DTOR
      ENDIF
C
C---- GDB: dump RLX, DAC, CL, QNEW[1]
      WRITE(0,'(A,Z8,A,Z8,A,Z8,A,Z8,A,Z8)')
     & 'F_UPDATE RLX=',TRANSFER(RLX,1),
     & ' DAC=',TRANSFER(DAC,1),
     & ' CLNEW=',TRANSFER(CLNEW,1),
     & ' QNEW1=',TRANSFER(QNEW(1),1),
     & ' QNEW40=',TRANSFER(QNEW(40),1)
C---- update BL variables with underrelaxed changes
      DO 5 IS=1, 2
        DO 50 IBL=2, NBL(IS)
          IV = ISYS(IBL,IS)
C
          DCTAU = VDEL(1,1,IV) - DAC*VDEL(1,2,IV)
          DTHET = VDEL(2,1,IV) - DAC*VDEL(2,2,IV)
          DMASS = VDEL(3,1,IV) - DAC*VDEL(3,2,IV)
          DUEDG = UNEW(IBL,IS) + DAC*U_AC(IBL,IS)  -  UEDG(IBL,IS)
          DDSTR = (DMASS - DSTR(IBL,IS)*DUEDG)/UEDG(IBL,IS)
          IF(UPDATE_COUNT.EQ.1 .AND. IS.EQ.2 .AND.
     &       IBL.GE.2 .AND. IBL.LE.4) THEN
           WRITE(0,'(A,I2,A,I3,A,Z8,A,Z8,A,Z8,A,Z8,A,Z8,
     &      A,Z8,A,Z8)')
     &      'F_UPD_APPLY0 U=',UPDATE_COUNT,' I=',IBL,
     &      ' OC=',TRANSFER(CTAU(IBL,IS),1),
     &      ' OT=',TRANSFER(THET(IBL,IS),1),
     &      ' OD=',TRANSFER(DSTR(IBL,IS),1),
     &      ' OU=',TRANSFER(UEDG(IBL,IS),1),
     &      ' RLX=',TRANSFER(RLX,1),
     &      ' DU=',TRANSFER(DUEDG,1),
     &      ' DD=',TRANSFER(DDSTR,1)
          ENDIF
C
          IF(IS.EQ.2 .AND. IBL.EQ.5) THEN
           WRITE(*,'(A,A,Z8,A,Z8,A,Z8,A,Z8,A,Z8)')
     &      'F_UPD5_DT',
     &      ' V10=',TRANSFER(VDEL(2,1,IV),1),
     &      ' V11=',TRANSFER(VDEL(2,2,IV),1),
     &      ' DAC=',TRANSFER(DAC,1),
     &      ' DxV=',TRANSFER(DAC*VDEL(2,2,IV),1),
     &      ' DT=',TRANSFER(DTHET,1)
          ENDIF
          IF(IS.EQ.2 .AND. IBL.EQ.12
     &       .AND. UPDATE_COUNT.EQ.1) THEN
           WRITE(0,'(A,Z8,A,Z8,A,Z8,A,Z8)')
     &      'F_UPD12 OT=',TRANSFER(THET(IBL,IS),1),
     &      ' DT=',TRANSFER(DTHET,1),
     &      ' RLX=',TRANSFER(RLX,1),
     &      ' NT=',TRANSFER(THET(IBL,IS)+RLX*DTHET,1)
          ENDIF
          IF(IS.EQ.1 .AND. IBL.EQ.27
     &       .AND. UPDATE_COUNT.GE.7 .AND. UPDATE_COUNT.LE.8) THEN
           WRITE(0,'(A,I2,A,Z8,A,Z8,A,Z8,A,Z8,A,Z8,A,Z8)')
     &      'F_UPD27 u=',UPDATE_COUNT,
     &      ' OT=',TRANSFER(THET(IBL,IS),1),
     &      ' DT=',TRANSFER(DTHET,1),
     &      ' RLX=',TRANSFER(RLX,1),
     &      ' V20=',TRANSFER(VDEL(2,1,IV),1),
     &      ' OC=',TRANSFER(CTAU(IBL,IS),1),
     &      ' DC=',TRANSFER(DCTAU,1)
          ENDIF
          CTAU(IBL,IS) = CTAU(IBL,IS) + RLX*DCTAU
          THET(IBL,IS) = THET(IBL,IS) + RLX*DTHET
          DSTR(IBL,IS) = DSTR(IBL,IS) + RLX*DDSTR
          UEDG(IBL,IS) = UEDG(IBL,IS) + RLX*DUEDG
C
          IF(IBL.GT.IBLTE(IS)) THEN
           IW = IBL - IBLTE(IS)
           DSWAKI = WGAP(IW)
          ELSE
           DSWAKI = 0.
          ENDIF
C
C-------- eliminate absurd transients
          IF(IBL.GE.ITRAN(IS))
     &      CTAU(IBL,IS) = MIN( CTAU(IBL,IS) , 0.25 )
C
          IF(IBL.LE.IBLTE(IS)) THEN
            HKLIM = 1.02
          ELSE
            HKLIM = 1.00005
          ENDIF
          MSQ = UEDG(IBL,IS)**2*HSTINV
     &        / (GAMM1*(1.0 - 0.5*UEDG(IBL,IS)**2*HSTINV))
          DSW = DSTR(IBL,IS) - DSWAKI
          CALL DSLIM(DSW,THET(IBL,IS),UEDG(IBL,IS),MSQ,HKLIM)
          DSTR(IBL,IS) = DSW + DSWAKI
C
C-------- set new mass defect (nonlinear update)
          MASS(IBL,IS) = DSTR(IBL,IS) * UEDG(IBL,IS)
C-------- per-station post-UPDATE trace for iter 1 at first stations side 2
          IF(UPDATE_COUNT.EQ.1 .AND. IS.EQ.2 .AND.
     &       IBL.GE.2 .AND. IBL.LE.4) THEN
           WRITE(0,'(A,I3,A,Z8,A,Z8,A,Z8,A,Z8)')
     &      'F_UPDSTATE IBL=',IBL,
     &      ' NT=',TRANSFER(THET(IBL,IS),1),
     &      ' ND=',TRANSFER(DSTR(IBL,IS),1),
     &      ' NU=',TRANSFER(UEDG(IBL,IS),1),
     &      ' NM=',TRANSFER(MASS(IBL,IS),1)
          ENDIF
C-------- per-station post-UPDATE trace for iter 11 at wake stations near 66
          IF(UPDATE_COUNT.EQ.11 .AND. IS.EQ.2 .AND.
     &       IBL.GE.64 .AND. IBL.LE.70) THEN
           WRITE(0,'(A,I3,A,Z8,A,Z8,A,Z8,A,Z8,A,Z8,A,Z8,A,Z8)')
     &      'F_POST_UPD11 IBL=',IBL,
     &      ' NT=',TRANSFER(THET(IBL,IS),1),
     &      ' ND=',TRANSFER(DSTR(IBL,IS),1),
     &      ' NU=',TRANSFER(UEDG(IBL,IS),1),
     &      ' NM=',TRANSFER(MASS(IBL,IS),1),
     &      ' DT=',TRANSFER(DTHET,1),
     &      ' DD=',TRANSFER(DDSTR,1),
     &      ' DU=',TRANSFER(DUEDG,1)
          ENDIF
          IF(UPDATE_COUNT.EQ.1 .AND. IS.EQ.2 .AND.
     &       IBL.GE.2 .AND. IBL.LE.4) THEN
           WRITE(0,'(A,I2,A,I3,A,Z8,A,Z8,A,Z8,A,Z8,A,Z8,A,Z8)')
     &      'F_UPD_APPLY1 U=',UPDATE_COUNT,' I=',IBL,
     &      ' NC=',TRANSFER(CTAU(IBL,IS),1),
     &      ' NT=',TRANSFER(THET(IBL,IS),1),
     &      ' ND=',TRANSFER(DSTR(IBL,IS),1),
     &      ' NU=',TRANSFER(UEDG(IBL,IS),1),
     &      ' MASS=',TRANSFER(MASS(IBL,IS),1),
     &      ' DSW=',TRANSFER(DSW,1)
          ENDIF
          IF(IS.EQ.2 .AND. IBL.EQ.81) THEN
            WRITE(0,'(A,Z8,A,Z8,A,Z8,A,Z8,A,Z8)')
     &       'F_SETBL_WK81'//
     &       ' DSTR=',TRANSFER(DSTR(IBL,IS),1),
     &       ' UEDG=',TRANSFER(UEDG(IBL,IS),1),
     &       ' MASS=',TRANSFER(MASS(IBL,IS),1),
     &       ' DDSTR=',TRANSFER(DDSTR,1),
     &       ' DUEDG=',TRANSFER(DUEDG,1)
          ENDIF
C
   50   CONTINUE
C
C------ make sure there are no "islands" of negative Ue
        DO IBL = 3, IBLTE(IS)
          IF(UEDG(IBL-1,IS) .GT. 0.0 .AND.
     &       UEDG(IBL  ,IS) .LE. 0.0       ) THEN
           UEDG(IBL,IS) = UEDG(IBL-1,IS)
           MASS(IBL,IS) = DSTR(IBL,IS) * UEDG(IBL,IS)
          ENDIF
        ENDDO
    5 CONTINUE
C
C
C---- equate upper wake arrays to lower wake arrays
      DO 6 KBL=1, NBL(2)-IBLTE(2)
        CTAU(IBLTE(1)+KBL,1) = CTAU(IBLTE(2)+KBL,2)
        THET(IBLTE(1)+KBL,1) = THET(IBLTE(2)+KBL,2)
        DSTR(IBLTE(1)+KBL,1) = DSTR(IBLTE(2)+KBL,2)
        UEDG(IBLTE(1)+KBL,1) = UEDG(IBLTE(2)+KBL,2)
         TAU(IBLTE(1)+KBL,1) =  TAU(IBLTE(2)+KBL,2)
         DIS(IBLTE(1)+KBL,1) =  DIS(IBLTE(2)+KBL,2)
         CTQ(IBLTE(1)+KBL,1) =  CTQ(IBLTE(2)+KBL,2)
        DELT(IBLTE(1)+KBL,1) = DELT(IBLTE(2)+KBL,2)
        TSTR(IBLTE(1)+KBL,1) = TSTR(IBLTE(2)+KBL,2)
    6 CONTINUE
C
      RETURN
      END



      SUBROUTINE DSLIM(DSTR,THET,UEDG,MSQ,HKLIM)
      IMPLICIT REAL (A-H,M,O-Z)
C
      H = DSTR/THET
      CALL HKIN(H,MSQ,HK,HK_H,HK_M)
C
      DH = MAX( 0.0 , HKLIM-HK ) / HK_H
      DSTR = DSTR + DH*THET
C
      RETURN
      END



      SUBROUTINE BLPINI
      INCLUDE 'BLPAR.INC'
C
      SCCON = 5.6
      GACON = 6.70
      GBCON = 0.75
      GCCON = 18.0
      DLCON =  0.9
C
      CTRCON = 1.8
      CTRCEX = 3.3
C
      DUXCON = 1.0
C
      CTCON = 0.5/(GACON**2 * GBCON)
C
      CFFAC = 1.0
C
      RETURN
      END
