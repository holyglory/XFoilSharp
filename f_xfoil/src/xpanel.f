C***********************************************************************
C    Module:  xpanel.f
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


      SUBROUTINE APCALC
      INCLUDE 'XFOIL.INC'
C
C---- set angles of airfoil panels
      DO 10 I=1, N-1
        SX = X(I+1) - X(I)
        SY = Y(I+1) - Y(I)
        IF(SX.EQ.0.0 .AND. SY.EQ.0.0) THEN
          APANEL(I) = ATAN2( -NY(I) , -NX(I) )
        ELSE
          APANEL(I) = ATAN2( SX , -SY )
        ENDIF
   10 CONTINUE
C
C---- TE panel
      I = N
      IP = 1
      IF(SHARP) THEN
       APANEL(I) = PI
      ELSE
       SX = X(IP) - X(I)
       SY = Y(IP) - Y(I)
       APANEL(I) = ATAN2( -SX , SY ) + PI
      ENDIF
C
      RETURN
      END
 
 
      SUBROUTINE NCALC(X,Y,S,N,XN,YN)
C---------------------------------------
C     Calculates normal unit vector
C     components at airfoil panel nodes
C---------------------------------------
      DIMENSION X(N), Y(N), S(N), XN(N), YN(N)
C
      IF(N.LE.1) RETURN
C
      CALL SEGSPL(X,XN,S,N)
      CALL SEGSPL(Y,YN,S,N)
C---- GDB: Trace spline derivs at a few nodes BEFORE normal
      DO 9 ITR=1, 5
        WRITE(0,'(A,I4,A,Z8,A,Z8,A,Z8,A,Z8,A,Z8)')
     &   'F_SPLN i=',ITR,
     &   ' x=',TRANSFER(X(ITR),1),
     &   ' y=',TRANSFER(Y(ITR),1),
     &   ' s=',TRANSFER(S(ITR),1),
     &   ' xp=',TRANSFER(XN(ITR),1),
     &   ' yp=',TRANSFER(YN(ITR),1)
 9    CONTINUE
      DO 10 I=1, N
        SX =  YN(I)
        SY = -XN(I)
        SMOD = SQRT(SX*SX + SY*SY)
        XN(I) = SX/SMOD
        YN(I) = SY/SMOD
C---- GDB: Trace NCALC output at a few nodes
        IF(I.LE.5 .OR. I.EQ.80) THEN
         WRITE(0,'(A,I4,A,Z8,A,Z8,A,Z8,A,Z8)')
     &    'F_NCALC i=',I,
     &    ' sx=',TRANSFER(SX,1),
     &    ' sy=',TRANSFER(SY,1),
     &    ' mag=',TRANSFER(SMOD,1),
     &    ' nx=',TRANSFER(XN(I),1)
        ENDIF
   10 CONTINUE
C
C---- average normal vectors at corner points
      DO 20 I=1, N-1
        IF(S(I) .EQ. S(I+1)) THEN
          SX = 0.5*(XN(I) + XN(I+1))
          SY = 0.5*(YN(I) + YN(I+1))
          SMOD = SQRT(SX*SX + SY*SY)
          XN(I)   = SX/SMOD
          YN(I)   = SY/SMOD
          XN(I+1) = SX/SMOD
          YN(I+1) = SY/SMOD
        ENDIF
 20   CONTINUE
C
      RETURN
      END

 
      SUBROUTINE PSILIN(I,XI,YI,NXI,NYI,PSI,PSI_NI,GEOLIN,SIGLIN)
C-----------------------------------------------------------------------
C     Calculates current streamfunction Psi at panel node or wake node
C     I due to freestream and all bound vorticity Gam on the airfoil. 
C     Sensitivities of Psi with respect to alpha (Z_ALFA) and inverse
C     Qspec DOFs (Z_QDOF0,Z_QDOF1) which influence Gam in inverse cases.
C     Also calculates the sensitivity vector dPsi/dGam (DZDG).
C
C     If SIGLIN=True, then Psi includes the effects of the viscous
C     source distribution Sig and the sensitivity vector dPsi/dSig
C     (DZDM) is calculated.
C
C     If GEOLIN=True, then the geometric sensitivity vector dPsi/dn
C     is calculated, where n is the normal motion of the jth node.
C
C          Airfoil:  1   < I < N
C          Wake:     N+1 < I < N+NW
C-----------------------------------------------------------------------
      INCLUDE 'XFOIL.INC'
      REAL NXO, NYO, NXP, NYP, NXI, NYI
      REAL PSIFSD, PSINIFSD
      LOGICAL GEOLIN,SIGLIN
C
C---- distance tolerance for determining if two points are the same
      SEPS = (S(N)-S(1)) * 1.0E-5
C
      IO = I
C
      CALL TRACE_ENTER('PSILIN')
      CALL TRACE_PSILIN_FIELD('PSILIN', IO, XI, YI, NXI, NYI,
     &                        GEOLIN, SIGLIN)
C
      COSA = COS(ALFA)
      SINA = SIN(ALFA)
      IF(IO.EQ.N+1 .AND. NXI.GT.0.5) THEN
       WRITE(0,'(A,Z8,A,Z8,A,Z8)')
     &  'F_CSNI ALFA=',TRANSFER(ALFA,1),
     &  ' COSA=',TRANSFER(COSA,1),
     &  ' SINA=',TRANSFER(SINA,1)
      ENDIF
C
      DO 3 JO=1, N
        DZDG(JO) = 0.0
        DZDN(JO) = 0.0
        DQDG(JO) = 0.0
    3 CONTINUE
C
      DO 4 JO=1, N
        DZDM(JO) = 0.0
        DQDM(JO) = 0.0
    4 CONTINUE
C
      Z_QINF = 0.
      Z_ALFA = 0.
      Z_QDOF0 = 0.
      Z_QDOF1 = 0.
      Z_QDOF2 = 0.
      Z_QDOF3 = 0.
C
      PSI    = 0.
      PSI_NI = 0.
C
      QTAN1 = 0.
      QTAN2 = 0.
      QTANM = 0.
C
      IF(SHARP) THEN
       SCS = 1.0
       SDS = 0.0
      ELSE
       SCS = ANTE/DSTE
       SDS = ASTE/DSTE
      ENDIF
C
      DO 10 JO=1, N
        JP = JO+1
C
        JM = JO-1
        JQ = JP+1
C
        IF(JO.EQ.1) THEN
         JM = JO
        ELSE IF(JO.EQ.N-1) THEN
         JQ = JP
        ELSE IF(JO.EQ.N) THEN
         JP = 1
         IF((X(JO)-X(JP))**2 + (Y(JO)-Y(JP))**2 .LT. SEPS**2) GO TO 12
        ENDIF
C
        DSO = SQRT((X(JO)-X(JP))**2 + (Y(JO)-Y(JP))**2)
C
C------ skip null panel
        IF(DSO .EQ. 0.0) GO TO 10
C
        DSIO = 1.0 / DSO
C
        APAN = APANEL(JO)
C
        RX1 = XI - X(JO)
        RY1 = YI - Y(JO)
        RX2 = XI - X(JP)
        RY2 = YI - Y(JP)
C
        SX = (X(JP) - X(JO)) * DSIO
        SY = (Y(JP) - Y(JO)) * DSIO
C
        X1 = SX*RX1 + SY*RY1
        X2 = SX*RX2 + SY*RY2
        YY = SX*RY1 - SY*RX1
C
        RS1 = RX1*RX1 + RY1*RY1
        RS2 = RX2*RX2 + RY2*RY2
C
C------ set reflection flag SGN to avoid branch problems with arctan
        IF(IO.GE.1 .AND. IO.LE.N) THEN
C------- no problem on airfoil surface
         SGN = 1.0
        ELSE
C------- make sure arctan falls between  -/+  Pi/2
         SGN = SIGN(1.0,YY)
        ENDIF
C
C------ set log(r^2) and arctan(x/y), correcting for reflection if any
        IF(IO.NE.JO .AND. RS1.GT.0.0) THEN
         G1 = LOG(RS1)
         T1 = ATAN2(SGN*X1,SGN*YY) + (0.5 - 0.5*SGN)*PI
        ELSE
         G1 = 0.0
         T1 = 0.0
        ENDIF
C
        IF(IO.NE.JP .AND. RS2.GT.0.0) THEN
         G2 = LOG(RS2)
         T2 = ATAN2(SGN*X2,SGN*YY) + (0.5 - 0.5*SGN)*PI
        ELSE
         G2 = 0.0
         T2 = 0.0
        ENDIF
C
        X1I = SX*NXI + SY*NYI
        X2I = SX*NXI + SY*NYI
        YYI = SX*NYI - SY*NXI
        CALL TRACE_PSILIN_PANEL('PSILIN', IO, JO,
     &       JM, JO, JP, JQ, GEOLIN, SIGLIN,
     &       X(JO), Y(JO), X(JP), Y(JP), X(JO)-X(JP), Y(JO)-Y(JP),
     &       DSO, DSIO, APAN,
     &       RX1, RY1, RX2, RY2, SX, SY,
     &       X1, X2, YY, RS1, RS2, SGN,
     &       G1, G2, T1, T2, X1I, X2I, YYI)
C
        IF(GEOLIN) THEN
         NXO = NX(JO)
         NYO = NY(JO)
         NXP = NX(JP)
         NYP = NY(JP)
C
         X1O =-((RX1-X1*SX)*NXO + (RY1-X1*SY)*NYO)*DSIO-(SX*NXO+SY*NYO)
         X1P = ((RX1-X1*SX)*NXP + (RY1-X1*SY)*NYP)*DSIO
         X2O =-((RX2-X2*SX)*NXO + (RY2-X2*SY)*NYO)*DSIO
         X2P = ((RX2-X2*SX)*NXP + (RY2-X2*SY)*NYP)*DSIO-(SX*NXP+SY*NYP)
         YYO = ((RX1+X1*SY)*NYO - (RY1-X1*SX)*NXO)*DSIO-(SX*NYO-SY*NXO)
         YYP =-((RX1-X1*SY)*NYP - (RY1+X1*SX)*NXP)*DSIO
        ENDIF
C
        IF(JO.EQ.N) GO TO 11
C
        IF(SIGLIN) THEN
C
C------- set up midpoint quantities
         X0 = 0.5*(X1+X2)
         RS0 = X0*X0 + YY*YY
         G0 = LOG(RS0)
         T0 = ATAN2(SGN*X0,SGN*YY) + (0.5 - 0.5*SGN)*PI
C
C------- calculate source contribution to Psi  for  1-0  half-panel
         DXINV = 1.0/(X1-X0)
         PSUMTERM1 = X0*(T0-APAN)
         PSUMTERM2 = X1*(T1-APAN)
         PSUMTERM3 = 0.5*YY*(G1-G0)
         PSUMACCUM = PSUMTERM1 - PSUMTERM2
         PSUM = PSUMACCUM + PSUMTERM3
         PDIFTERM1 = (X1+X0)*PSUM
         PDIFTERM2 = RS1*(T1-APAN)
         PDIFTERM3 = RS0*(T0-APAN)
         PDIFTERM4 = (X0-X1)*YY
         PDIFACCUM1 = PDIFTERM1 + PDIFTERM2
         PDIFACCUM2 = PDIFACCUM1 - PDIFTERM3
         PDIFNUM = PDIFACCUM2 + PDIFTERM4
         PDIF = PDIFNUM * DXINV
         CALL TRACE_PSILIN_SOURCE_HALF_TERMS('PSILIN', IO, JO, 1,
     &        X0,
     &        PSUMTERM1, PSUMTERM2, PSUMTERM3, PSUMACCUM, PSUM,
     &        PDIFTERM1, PDIFTERM2, PDIFTERM3, PDIFTERM4,
     &        PDIFACCUM1, PDIFACCUM2, PDIFNUM, PDIF)
C
         PSX1 =  -(T1-APAN)
         PSX0 =    T0-APAN
         PSYY =  0.5*(G1-G0)
C
         PDX0TERM1 = (X1+X0)*PSX0
         PDX0TERM2 = -2.0*X0*(T0-APAN)
         PDX0NUMERATOR = (PDX0TERM1 + PSUM) + PDX0TERM2
         PDX0NUMERATOR = PDX0NUMERATOR + PDIF
         PDX1TERM1 = (X1+X0)*PSX1
         PDX1TERM2 = 2.0*X1*(T1-APAN)
         PDX1NUMERATOR = (PDX1TERM1 + PSUM) + PDX1TERM2
         PDX1NUMERATOR = PDX1NUMERATOR - PDIF
         PDYYTERM1 = (X1+X0)*PSYY
         PDYYTAILLINEAR = 2.0*(X0-X1)
         PDYYTAILANGULAR = 2.0*YY*(T1-T0)
         PDYYTERM2 = PDYYTAILLINEAR + PDYYTAILANGULAR
         PDYYNUMERATOR = PDYYTERM1 + PDYYTERM2
         PDX1 = ((X1+X0)*PSX1 + PSUM + 2.0*X1*(T1-APAN) - PDIF) * DXINV
         PDX0 = ((X1+X0)*PSX0 + PSUM - 2.0*X0*(T0-APAN) + PDIF) * DXINV
         PDYY = ((X1+X0)*PSYY + 2.0*(X0-X1 + YY*(T1-T0))      ) * DXINV
C
         DSM = SQRT((X(JP)-X(JM))**2 + (Y(JP)-Y(JM))**2)
         DSIM = 1.0/DSM
C
CCC      SIG0 = (SIG(JP) - SIG(JO))*DSIO
CCC      SIG1 = (SIG(JP) - SIG(JM))*DSIM
CCC      SSUM = SIG0 + SIG1
CCC      SDIF = SIG0 - SIG1
C
         SOURCETERMLEFT = (SIG(JP) - SIG(JO))*DSIO
         SOURCETERMRIGHT = (SIG(JP) - SIG(JM))*DSIM
         SSUM = (SIG(JP) - SIG(JO))*DSIO + (SIG(JP) - SIG(JM))*DSIM
         SDIF = (SIG(JP) - SIG(JO))*DSIO - (SIG(JP) - SIG(JM))*DSIM
C
         PSIBEF = PSI
         PSINIBEF = PSI_NI
         PSI = PSI + QOPI*(PSUM*SSUM + PDIF*SDIF)
C
C------- dPsi/dm
         DZJM = QOPI*(-PSUM*DSIM + PDIF*DSIM)
         DZJO = QOPI*(-PSUM*DSIO - PDIF*DSIO)
         DZJP = QOPI*( PSUM*(DSIO+DSIM) + PDIF*(DSIO-DSIM))
         DZDM(JM) = DZDM(JM) + DZJM
         DZDM(JO) = DZDM(JO) + DZJO
         DZDM(JP) = DZDM(JP) + DZJP
         CALL TRACE_PSILIN_SOURCE_DZ_TERMS('PSILIN', IO, JO, 1,
     &        -PSUM*DSIM, PDIF*DSIM, (-PSUM*DSIM + PDIF*DSIM),
     &        -PSUM*DSIO, -PDIF*DSIO, (-PSUM*DSIO - PDIF*DSIO),
     &        PSUM*(DSIO+DSIM), PDIF*(DSIO-DSIM),
     &        ( PSUM*(DSIO+DSIM) + PDIF*(DSIO-DSIM)),
     &        0.0, 0.0, 0.0)
C
C------- dPsi/dni
         PSNITERM1 = PSX1*X1I
         PSNITERM2 = PSX0*(X1I+X2I)*0.5
         PSNITERM3 = PSYY*YYI
         PSNI = PSX1*X1I + PSX0*(X1I+X2I)*0.5 + PSYY*YYI
         PDNITERM1 = PDX1*X1I
         PDNITERM2 = PDX0*(X1I+X2I)*0.5
         PDNITERM3 = PDYY*YYI
         PDNI = PDX1*X1I + PDX0*(X1I+X2I)*0.5 + PDYY*YYI
         PSI_NI = PSI_NI + QOPI*(PSNI*SSUM + PDNI*SDIF)
         CALL TRACE_PSILIN_ACCUM_STATE('PSILIN', IO, 'source_half1',
     &                               JO, JP, PSIBEF, PSINIBEF,
     &                               PSI, PSI_NI)
C
         QTANM = QTANM + QOPI*(PSNI*SSUM + PDNI*SDIF)
C
         DQJM = QOPI*(-PSNI*DSIM + PDNI*DSIM)
         DQJO = QOPI*(-PSNI*DSIO - PDNI*DSIO)
         DQJP = QOPI*( PSNI*(DSIO+DSIM) + PDNI*(DSIO-DSIM))
         DQDM(JM) = DQDM(JM) + DQJM
         DQDM(JO) = DQDM(JO) + DQJO
         DQDM(JP) = DQDM(JP) + DQJP
         CALL TRACE_PSILIN_SOURCE_DQ_TERMS('PSILIN', IO, JO, 1,
     &        -PSNI*DSIM, PDNI*DSIM, (-PSNI*DSIM + PDNI*DSIM),
     &        -PSNI*DSIO, -PDNI*DSIO, (-PSNI*DSIO - PDNI*DSIO),
     &        PSNI*(DSIO+DSIM), PDNI*(DSIO-DSIM),
     &        ( PSNI*(DSIO+DSIM) + PDNI*(DSIO-DSIM)),
     &        0.0, 0.0, 0.0)
         CALL TRACE_PSILIN_SOURCE_SEGMENT('PSILIN', IO, JO, 1,
     &        JM, JO, JP, JQ,
     &        X0, X1, X2, YY, APAN, X1I, X2I, YYI,
     &        RS0, RS1, RS2, G0, G1, G2, T0, T1, T2,
     &        DSO, DSIO, DSM, DSIM, 0.0, 0.0, DXINV,
     &        SOURCETERMLEFT, SOURCETERMRIGHT,
     &        SSUM, SDIF, PSUM, PDIF,
     &        PSX0, PSX1, 0.0, PSYY,
     &        PDX0TERM1, PDX0TERM2, PDX0NUMERATOR, PDX0,
     &        PDX1TERM1, PDX1TERM2, PDX1NUMERATOR, PDX1,
     &        0.0, 0.0, 0.0, 0.0,
     &        PDYYTERM1, PDYYTAILLINEAR, PDYYTAILANGULAR,
     &        PDYYTERM2, PDYYNUMERATOR, PDYY,
     &        PSNITERM1, PSNITERM2, PSNITERM3, PSNI,
     &        PDNITERM1, PDNITERM2, PDNITERM3, PDNI,
     &        DZJM, DZJO, DZJP, 0.0,
     &        DQJM, DQJO, DQJP, 0.0)
         CALL TRACE_PSILIN_SOURCE_PDYY_WRITE('PSILIN', IO, JO, 1,
     &        X0, X1, YY, T0, T1, PSYY, DXINV,
     &        (T1-T0),
     &        (X0-X1 + YY*(T1-T0)),
     &        ((X1+X0)*PSYY),
     &        (2.0*(X0-X1 + YY*(T1-T0))),
     &        (((X1+X0)*PSYY) + (2.0*(X0-X1 + YY*(T1-T0)))),
     &        ((((X1+X0)*PSYY) + (2.0*(X0-X1 + YY*(T1-T0))))*DXINV),
     &        PDYYTERM1, PDYYTERM2, PDYYNUMERATOR, PDYY)
C
C
C------- calculate source contribution to Psi  for  0-2  half-panel
         DXINV = 1.0/(X0-X2)
         PSUMTERM1 = X2*(T2-APAN)
         PSUMTERM2 = X0*(T0-APAN)
         PSUMTERM3 = 0.5*YY*(G0-G2)
         PSUMACCUM = PSUMTERM1 - PSUMTERM2
         PSUM = PSUMACCUM + PSUMTERM3
         PDIFTERM1 = (X0+X2)*PSUM
         PDIFTERM2 = RS0*(T0-APAN)
         PDIFTERM3 = RS2*(T2-APAN)
         PDIFTERM4 = (X2-X0)*YY
         PDIFACCUM1 = PDIFTERM1 + PDIFTERM2
         PDIFACCUM2 = PDIFACCUM1 - PDIFTERM3
         PDIFNUM = PDIFACCUM2 + PDIFTERM4
         PDIF = PDIFNUM * DXINV
         CALL TRACE_PSILIN_SOURCE_HALF_TERMS('PSILIN', IO, JO, 2,
     &        X0,
     &        PSUMTERM1, PSUMTERM2, PSUMTERM3, PSUMACCUM, PSUM,
     &        PDIFTERM1, PDIFTERM2, PDIFTERM3, PDIFTERM4,
     &        PDIFACCUM1, PDIFACCUM2, PDIFNUM, PDIF)
C
         PSX0 =  -(T0-APAN)
         PSX2 =    T2-APAN
         PSYY =  0.5*(G0-G2)
C
         PDX0TERM1 = (X0+X2)*PSX0
         PDX0TERM2 = 2.0*X0*(T0-APAN)
         PDX0NUMERATOR = (PDX0TERM1 + PSUM) + PDX0TERM2
         PDX0NUMERATOR = PDX0NUMERATOR - PDIF
         PDX2TERM1 = (X0+X2)*PSX2
         PDX2TERM2 = -2.0*X2*(T2-APAN)
         PDX2NUMERATOR = (PDX2TERM1 + PSUM) + PDX2TERM2
         PDX2NUMERATOR = PDX2NUMERATOR + PDIF
         PDYYTERM1 = (X0+X2)*PSYY
         PDYYTAILLINEAR = 2.0*(X2-X0)
         PDYYTAILANGULAR = 2.0*YY*(T0-T2)
         PDYYTERM2 = PDYYTAILLINEAR + PDYYTAILANGULAR
         PDYYNUMERATOR = PDYYTERM1 + PDYYTERM2
         PDX0 = ((X0+X2)*PSX0 + PSUM + 2.0*X0*(T0-APAN) - PDIF) * DXINV
         PDX2 = ((X0+X2)*PSX2 + PSUM - 2.0*X2*(T2-APAN) + PDIF) * DXINV
         PDYY = ((X0+X2)*PSYY + 2.0*(X2-X0 + YY*(T0-T2))      ) * DXINV
C
         DSP = SQRT((X(JQ)-X(JO))**2 + (Y(JQ)-Y(JO))**2)
         DSIP = 1.0/DSP
C
CCC         SIG2 = (SIG(JQ) - SIG(JO))*DSIP
CCC         SIG0 = (SIG(JP) - SIG(JO))*DSIO
CCC         SSUM = SIG2 + SIG0
CCC         SDIF = SIG2 - SIG0
C
         SOURCETERMLEFT = (SIG(JQ) - SIG(JO))*DSIP
         SOURCETERMRIGHT = (SIG(JP) - SIG(JO))*DSIO
         SSUM = (SIG(JQ) - SIG(JO))*DSIP + (SIG(JP) - SIG(JO))*DSIO
         SDIF = (SIG(JQ) - SIG(JO))*DSIP - (SIG(JP) - SIG(JO))*DSIO
C
         PSIBEF = PSI
         PSINIBEF = PSI_NI
         PSI = PSI + QOPI*(PSUM*SSUM + PDIF*SDIF)
C
C------- dPsi/dm
         DZJO = QOPI*(-PSUM*(DSIP+DSIO) - PDIF*(DSIP-DSIO))
         DZJP = QOPI*( PSUM*DSIO - PDIF*DSIO)
         DZJQ = QOPI*( PSUM*DSIP + PDIF*DSIP)
         DZDM(JO) = DZDM(JO) + DZJO
         DZDM(JP) = DZDM(JP) + DZJP
         DZDM(JQ) = DZDM(JQ) + DZJQ
         CALL TRACE_PSILIN_SOURCE_DZ_TERMS('PSILIN', IO, JO, 2,
     &        0.0, 0.0, 0.0,
     &        -PSUM*(DSIP+DSIO), -PDIF*(DSIP-DSIO),
     &        (-PSUM*(DSIP+DSIO) - PDIF*(DSIP-DSIO)),
     &        PSUM*DSIO, -PDIF*DSIO, (PSUM*DSIO - PDIF*DSIO),
     &        PSUM*DSIP, PDIF*DSIP, (PSUM*DSIP + PDIF*DSIP))
C
C------- dPsi/dni
         PSNITERM1 = PSX0*(X1I+X2I)*0.5
         PSNITERM2 = PSX2*X2I
         PSNITERM3 = PSYY*YYI
         PSNI = PSX0*(X1I+X2I)*0.5 + PSX2*X2I + PSYY*YYI
         PDNITERM1 = PDX0*(X1I+X2I)*0.5
         PDNITERM2 = PDX2*X2I
         PDNITERM3 = PDYY*YYI
         PDNI = PDX0*(X1I+X2I)*0.5 + PDX2*X2I + PDYY*YYI
         PSI_NI = PSI_NI + QOPI*(PSNI*SSUM + PDNI*SDIF)
         CALL TRACE_PSILIN_ACCUM_STATE('PSILIN', IO, 'source_half2',
     &                               JO, JP, PSIBEF, PSINIBEF,
     &                               PSI, PSI_NI)
C
         QTANM = QTANM + QOPI*(PSNI*SSUM + PDNI*SDIF)
C
         DQJO = QOPI*(-PSNI*(DSIP+DSIO) - PDNI*(DSIP-DSIO))
         DQJP = QOPI*( PSNI*DSIO - PDNI*DSIO)
         DQJQ = QOPI*( PSNI*DSIP + PDNI*DSIP)
         DQDM(JO) = DQDM(JO) + DQJO
         DQDM(JP) = DQDM(JP) + DQJP
         DQDM(JQ) = DQDM(JQ) + DQJQ
         CALL TRACE_PSILIN_SOURCE_DQ_TERMS('PSILIN', IO, JO, 2,
     &        0.0, 0.0, 0.0,
     &        -PSNI*(DSIP+DSIO), -PDNI*(DSIP-DSIO),
     &        (-PSNI*(DSIP+DSIO) - PDNI*(DSIP-DSIO)),
     &        PSNI*DSIO, -PDNI*DSIO, (PSNI*DSIO - PDNI*DSIO),
     &        PSNI*DSIP, PDNI*DSIP, (PSNI*DSIP + PDNI*DSIP))
         CALL TRACE_PSILIN_SOURCE_SEGMENT('PSILIN', IO, JO, 2,
     &        JM, JO, JP, JQ,
     &        X0, X1, X2, YY, APAN, X1I, X2I, YYI,
     &        RS0, RS1, RS2, G0, G1, G2, T0, T1, T2,
     &        DSO, DSIO, 0.0, 0.0, DSP, DSIP, DXINV,
     &        SOURCETERMLEFT, SOURCETERMRIGHT,
     &        SSUM, SDIF, PSUM, PDIF,
     &        PSX0, 0.0, PSX2, PSYY,
     &        PDX0TERM1, PDX0TERM2, PDX0NUMERATOR, PDX0,
     &        0.0, 0.0, 0.0, 0.0,
     &        PDX2TERM1, PDX2TERM2, PDX2NUMERATOR, PDX2,
     &        PDYYTERM1, PDYYTAILLINEAR, PDYYTAILANGULAR,
     &        PDYYTERM2, PDYYNUMERATOR, PDYY,
     &        PSNITERM1, PSNITERM2, PSNITERM3, PSNI,
     &        PDNITERM1, PDNITERM2, PDNITERM3, PDNI,
     &        0.0, DZJO, DZJP, DZJQ,
     &        0.0, DQJO, DQJP, DQJQ)
         CALL TRACE_PSILIN_SOURCE_PDYY_WRITE('PSILIN', IO, JO, 2,
     &        X0, X2, YY, T0, T2, PSYY, DXINV,
     &        (T0-T2),
     &        (X2-X0 + YY*(T0-T2)),
     &        ((X0+X2)*PSYY),
     &        (2.0*(X2-X0 + YY*(T0-T2))),
     &        (((X0+X2)*PSYY) + (2.0*(X2-X0 + YY*(T0-T2)))),
     &        ((((X0+X2)*PSYY) + (2.0*(X2-X0 + YY*(T0-T2))))*DXINV),
     &        PDYYTERM1, PDYYTERM2, PDYYNUMERATOR, PDYY)
C
        ENDIF
C
C------ calculate vortex panel contribution to Psi
        DXINV = 1.0/(X1-X2)
        PSIS = 0.5*X1*G1 - 0.5*X2*G2 + X2 - X1 + YY*(T1-T2)
        PSID = ((X1+X2)*PSIS + 0.5*(RS2*G2-RS1*G1 + X1*X1-X2*X2))*DXINV
C
        PSX1 = 0.5*G1
        PSX2 = -.5*G2
        PSYY = T1-T2
C
        PDX1 = ((X1+X2)*PSX1 + PSIS - X1*G1 - PSID)*DXINV
        PDX2 = ((X1+X2)*PSX2 + PSIS + X2*G2 + PSID)*DXINV
        PDYY = ((X1+X2)*PSYY - YY*(G1-G2)         )*DXINV
C
        GSUM1 = GAMU(JP,1) + GAMU(JO,1)
        GSUM2 = GAMU(JP,2) + GAMU(JO,2)
        GDIF1 = GAMU(JP,1) - GAMU(JO,1)
        GDIF2 = GAMU(JP,2) - GAMU(JO,2)
C
        GSUM = GAM(JP) + GAM(JO)
        GDIF = GAM(JP) - GAM(JO)
C
        PSIBEF = PSI
        PSINIBEF = PSI_NI
        ZVOR = QOPI*(PSIS*GSUM + PSID*GDIF)
        PSI = PSI + ZVOR
C
C------ dPsi/dGam
        DZGJO = QOPI*(PSIS-PSID)
        DZGJP = QOPI*(PSIS+PSID)
        DZDG(JO) = DZDG(JO) + DZGJO
        DZDG(JP) = DZDG(JP) + DZGJP
C---- GDB: trace DZDG(1) at field node 33
        IF(IO.EQ.33 .AND. (JO.EQ.1.OR.JO.EQ.N-1)) THEN
          WRITE(0,'(A,I3,A,I3,A,Z8,A,Z8,A,Z8,A,Z8,A,Z8)')
     &     'F_DZDG0 io=',IO,' jo=',JO,
     &     ' dzdg1=',TRANSFER(DZDG(1),1),
     &     ' dzgjo=',TRANSFER(DZGJO,1),
     &     ' dzgjp=',TRANSFER(DZGJP,1),
     &     ' psis=',TRANSFER(PSIS,1),
     &     ' psid=',TRANSFER(PSID,1)
        ENDIF
C
C------ dPsi/dni
        PSNI = PSX1*X1I + PSX2*X2I + PSYY*YYI
        PDNI = PDX1*X1I + PDX2*X2I + PDYY*YYI
        QVOR = QOPI*(GSUM*PSNI + GDIF*PDNI)
        PSI_NI = PSI_NI + QVOR
        IF(IO.EQ.N+1 .AND. NXI.GT.0.5 .AND. JO.GE.141) THEN
         WRITE(0,'(A,I4,A,Z8)')
     &    'F_WK0X jo=',JO,' ni=',TRANSFER(PSI_NI,1)
        ENDIF
        IF(IO.EQ.N+6 .AND. NXI.GT.0.5) THEN
         IF(JO.GE.65 .AND. JO.LE.70) THEN
          WRITE(0,'(A,I4,A,Z8,A,Z8,A,Z8,A,Z8,A,Z8)')
     &     'F_PSI6D jo=',JO,
     &     ' ni_b=',TRANSFER(PSI_NI-QVOR,1),
     &     ' delt=',TRANSFER(QVOR,1),
     &     ' ni=',TRANSFER(PSI_NI,1),
     &     ' gsum=',TRANSFER(GSUM,1),
     &     ' gdif=',TRANSFER(GDIF,1)
         ELSE
          WRITE(0,'(A,I4,A,Z8)')
     &     'F_PSI6X jo=',JO,' ni=',TRANSFER(PSI_NI,1)
         ENDIF
        ENDIF
        IF(IO.EQ.N+7 .AND. (JO.EQ.1.OR.JO.EQ.40
     &     .OR.JO.EQ.80.OR.JO.EQ.120.OR.JO.EQ.N)) THEN
         WRITE(0,'(A,I4,A,Z8)')
     &    'F_PSACC167 jo=',JO,' ni=',TRANSFER(PSI_NI,1)
        ENDIF
        CALL TRACE_PSILIN_ACCUM_STATE('PSILIN', IO, 'vortex_segment',
     &                               JO, JP, PSIBEF, PSINIBEF,
     &                               PSI, PSI_NI)
C
        QTAN1 = QTAN1 + QOPI*(GSUM1*PSNI + GDIF1*PDNI)
        QTAN2 = QTAN2 + QOPI*(GSUM2*PSNI + GDIF2*PDNI)
        IF(IO.EQ.N+7 .AND. (JO.EQ.1.OR.JO.EQ.40
     &     .OR.JO.EQ.80.OR.JO.EQ.120.OR.JO.EQ.N)) THEN
         WRITE(0,'(A,I4,A,Z8)')
     &    'F_PSILIN167 jo=',JO,' q1=',TRANSFER(QTAN1,1)
        ENDIF
C
        DQGJO = QOPI*(PSNI - PDNI)
        DQGJP = QOPI*(PSNI + PDNI)
        DQDG(JO) = DQDG(JO) + DQGJO
        DQDG(JP) = DQDG(JP) + DQGJP
        PSIST1 = 0.5*X1*G1
        PSIST2 = -0.5*X2*G2
        PSIST3 = X2 - X1
        PSIST4 = YY*(T1-T2)
        PSIDT1 = (X1+X2)*PSIS
        PSIDT2 = RS2*G2
        PSIDT3 = RS1*G1
        PSIDT4 = X1*X1
        PSIDT5 = X2*X2
        PSIDH  = 0.5*(PSIDT2 - PSIDT3 + PSIDT4 - PSIDT5)
        PDXSUM = X1 + X2
        PDX1MUL = PDXSUM*PSX1
        PDX1PAN = X1*G1
        PDX1A1 = PDX1MUL + PSIS
        PDX1A2 = PDX1A1 - PDX1PAN
        PDX1NUM = PDX1A2 - PSID
        PDX2MUL = PDXSUM*PSX2
        PDX2PAN = X2*G2
        PDX2A1 = PDX2MUL + PSIS
        PDX2A2 = PDX2A1 + PDX2PAN
        PDX2NUM = PDX2A2 + PSID
        CALL TRACE_PSILIN_VORTEX_SEGMENT('PSILIN', IO, JO, JP,
     &       X1, X2, YY, RS1, RS2, G1, G2, T1, T2,
     &       DXINV,
     &       PSIST1, PSIST2, PSIST3, PSIST4, PSIS,
     &       PSIDT1, PSIDT2, PSIDT3, PSIDT4, PSIDT5, PSIDH, PSID,
     &       PSX1, PSX2, PSYY,
     &       PDXSUM, PDX1MUL, PDX1PAN, PDX1A1, PDX1A2, PDX1NUM,
     &       PDX1, PDX2MUL, PDX2PAN, PDX2A1, PDX2A2, PDX2NUM,
     &       PDX2, PDYY,
     &       GAM(JO), GAM(JP), GSUM, GDIF, PSNI, PDNI, ZVOR, QVOR,
     &       DZGJO, DZGJP, DQGJO, DQGJP)
C
        IF(GEOLIN) THEN
C
C------- dPsi/dn
         DZDN(JO) = DZDN(JO)+ QOPI*GSUM*(PSX1*X1O + PSX2*X2O + PSYY*YYO)
     &                      + QOPI*GDIF*(PDX1*X1O + PDX2*X2O + PDYY*YYO)
         DZDN(JP) = DZDN(JP)+ QOPI*GSUM*(PSX1*X1P + PSX2*X2P + PSYY*YYP)
     &                      + QOPI*GDIF*(PDX1*X1P + PDX2*X2P + PDYY*YYP)
C------- dPsi/dP
         Z_QDOF0 = Z_QDOF0
     &           + QOPI*((PSIS-PSID)*QF0(JO) + (PSIS+PSID)*QF0(JP))
         Z_QDOF1 = Z_QDOF1
     &           + QOPI*((PSIS-PSID)*QF1(JO) + (PSIS+PSID)*QF1(JP))
         Z_QDOF2 = Z_QDOF2
     &           + QOPI*((PSIS-PSID)*QF2(JO) + (PSIS+PSID)*QF2(JP))
         Z_QDOF3 = Z_QDOF3
     &           + QOPI*((PSIS-PSID)*QF3(JO) + (PSIS+PSID)*QF3(JP))
        ENDIF
C
C
   10 CONTINUE
C
   11 CONTINUE
      PSIG = 0.5*YY*(G1-G2) + X2*(T2-APAN) - X1*(T1-APAN)
      PGAM = 0.5*X1*G1 - 0.5*X2*G2 + X2 - X1 + YY*(T1-T2)
C
      PSIGX1 = -(T1-APAN)
      PSIGX2 =   T2-APAN
      PSIGYY = 0.5*(G1-G2)
      PGAMX1 = 0.5*G1
      PGAMX2 = -.5*G2
      PGAMYY = T1-T2
C
      PSIGNI = PSIGX1*X1I + PSIGX2*X2I + PSIGYY*YYI
      PGAMNI = PGAMX1*X1I + PGAMX2*X2I + PGAMYY*YYI
C
C---- TE panel source and vortex strengths
      SIGTE1 = 0.5*SCS*(GAMU(JP,1) - GAMU(JO,1))
      SIGTE2 = 0.5*SCS*(GAMU(JP,2) - GAMU(JO,2))
      GAMTE1 = -.5*SDS*(GAMU(JP,1) - GAMU(JO,1))
      GAMTE2 = -.5*SDS*(GAMU(JP,2) - GAMU(JO,2))
C
      SIGTE = 0.5*SCS*(GAM(JP) - GAM(JO))
      GAMTE = -.5*SDS*(GAM(JP) - GAM(JO))
C
C---- TE panel contribution to Psi
      PSIBEF = PSI
      PSINIBEF = PSI_NI
      PSI = PSI + HOPI*(PSIG*SIGTE + PGAM*GAMTE)
C
C---- dPsi/dGam
      DZGJOSIG = -HOPI*PSIG*SCS*0.5
      DZGJPSIG = HOPI*PSIG*SCS*0.5
      DZDG(JO) = DZDG(JO) + DZGJOSIG
      DZDG(JP) = DZDG(JP) + DZGJPSIG
C
      DZGJOGAM = HOPI*PGAM*SDS*0.5
      DZGJPGAM = -HOPI*PGAM*SDS*0.5
      DZDG(JO) = DZDG(JO) + DZGJOGAM
      DZDG(JP) = DZDG(JP) + DZGJPGAM
C---- GDB: dump DZDG(1) after TE at field 33
      IF(IO.EQ.33) THEN
        WRITE(0,'(A,Z8,A,Z8,A,Z8,A,Z8,A,Z8,A,Z8,A,Z8)')
     &   'F_TE33 dzdg1=',TRANSFER(DZDG(1),1),
     &   ' dzgjpsig=',TRANSFER(DZGJPSIG,1),
     &   ' dzgjpgam=',TRANSFER(DZGJPGAM,1),
     &   ' psig=',TRANSFER(PSIG,1),
     &   ' pgam=',TRANSFER(PGAM,1),
     &   ' scs=',TRANSFER(SCS,1),
     &   ' sds=',TRANSFER(SDS,1)
      ENDIF
C
C---- dPsi/dni
      PSI_NI = PSI_NI + HOPI*(PSIGNI*SIGTE + PGAMNI*GAMTE)
      CALL TRACE_PSILIN_ACCUM_STATE('PSILIN', IO, 'te_correction',
     &                              JO, JP, PSIBEF, PSINIBEF,
     &                              PSI, PSI_NI)
C
      QTAN1 = QTAN1 + HOPI*(PSIGNI*SIGTE1 + PGAMNI*GAMTE1)
      QTAN2 = QTAN2 + HOPI*(PSIGNI*SIGTE2 + PGAMNI*GAMTE2)
C
      DQGJOSIGHALF = PSIGNI*0.5
      DQGJOSIGTERM = DQGJOSIGHALF*SCS
      DQGJOGAMHALF = PGAMNI*0.5
      DQGJOGAMTERM = DQGJOGAMHALF*SDS
      DQGTEINNER = DQGJOSIGTERM - DQGJOGAMTERM
      DQGJOTE = -HOPI*(PSIGNI*0.5*SCS - PGAMNI*0.5*SDS)
      DQGJPTE = HOPI*(PSIGNI*0.5*SCS - PGAMNI*0.5*SDS)
      DQDG(JO) = DQDG(JO) + DQGJOTE
      DQDG(JP) = DQDG(JP) + DQGJPTE
      CALL TRACE_PSILIN_TE_CORRECTION('PSILIN', IO, JO, JP,
     &     PSIG, PGAM, PSIGNI, PGAMNI, SIGTE, GAMTE, SCS, SDS,
     &     DZGJOSIG, DZGJPSIG, DZGJOGAM, DZGJPGAM,
     &     DQGJOSIGHALF, DQGJOSIGTERM,
     &     DQGJOGAMHALF, DQGJOGAMTERM, DQGTEINNER,
     &     DQGJOTE, DQGJPTE)
      CALL TRACE_PSILIN_TE_PGAM_TERMS('PSILIN', IO, JO, JP,
     &     (0.5*X1*G1), (-0.5*X2*G2),
     &     (0.5*X1*G1 - 0.5*X2*G2),
     &     ((0.5*X1*G1 - 0.5*X2*G2) + X2 - X1),
     &     (T1-T2), (YY*(T1-T2)))
C
      IF(GEOLIN) THEN
C
C----- dPsi/dn
       DZDN(JO) = DZDN(JO)
     &          + HOPI*(PSIGX1*X1O + PSIGX2*X2O + PSIGYY*YYO)*SIGTE
     &          + HOPI*(PGAMX1*X1O + PGAMX2*X2O + PGAMYY*YYO)*GAMTE
       DZDN(JP) = DZDN(JP)
     &          + HOPI*(PSIGX1*X1P + PSIGX2*X2P + PSIGYY*YYP)*SIGTE
     &          + HOPI*(PGAMX1*X1P + PGAMX2*X2P + PGAMYY*YYP)*GAMTE
C
C----- dPsi/dP
       Z_QDOF0 = Z_QDOF0 + HOPI*PSIG*0.5*(QF0(JP)-QF0(JO))*SCS
     &                   - HOPI*PGAM*0.5*(QF0(JP)-QF0(JO))*SDS
       Z_QDOF1 = Z_QDOF1 + HOPI*PSIG*0.5*(QF1(JP)-QF1(JO))*SCS
     &                   - HOPI*PGAM*0.5*(QF1(JP)-QF1(JO))*SDS
       Z_QDOF2 = Z_QDOF2 + HOPI*PSIG*0.5*(QF2(JP)-QF2(JO))*SCS
     &                   - HOPI*PGAM*0.5*(QF2(JP)-QF2(JO))*SDS
       Z_QDOF3 = Z_QDOF3 + HOPI*PSIG*0.5*(QF3(JP)-QF3(JO))*SCS
     &                   - HOPI*PGAM*0.5*(QF3(JP)-QF3(JO))*SDS
C
      ENDIF
C
   12 CONTINUE
C
C**** Freestream terms
      PSIFSD = QINF*(COSA*YI - SINA*XI)
C
C---- dPsi/dn
      PSINIFSD = QINF*(COSA*NYI - SINA*NXI)
      CALL TRACE_PSILIN_RESULT_TERMS('PSILIN', IO, PSI, PSI_NI,
     &                               PSIFSD, PSINIFSD)
      PSI = PSI + PSIFSD
      PSI_NI = PSI_NI + PSINIFSD
C
      QTAN1 = QTAN1 + QINF*NYI
      QTAN2 = QTAN2 - QINF*NXI
      IF(IO.EQ.N+7) THEN
       WRITE(0,'(A,Z8,A,Z8,A,Z8)')
     &  'F_PSINI167 ni=',TRANSFER(PSI_NI,1),
     &  ' q1=',TRANSFER(QTAN1,1),
     &  ' q2=',TRANSFER(QTAN2,1)
      ENDIF
C
C---- dPsi/dQinf
      Z_QINF = Z_QINF + (COSA*YI - SINA*XI)
C
C---- dPsi/dalfa
      Z_ALFA = Z_ALFA - QINF*(SINA*YI + COSA*XI)
C
      IF(.NOT.LIMAGE) THEN
       CALL TRACE_PSILIN_RESULT('PSILIN', IO, PSI, PSI_NI)
       CALL TRACE_EXIT('PSILIN')
       RETURN
      ENDIF
C
C
C
      DO 20 JO=1, N
        JP = JO+1
C
        JM = JO-1
        JQ = JP+1
C
        IF(JO.EQ.1) THEN
         JM = JO
        ELSE IF(JO.EQ.N-1) THEN
         JQ = JP
        ELSE IF(JO.EQ.N) THEN
         JP = 1
         IF((X(JO)-X(JP))**2 + (Y(JO)-Y(JP))**2 .LT. SEPS**2) GO TO 22
        ENDIF
C
        DSO = SQRT((X(JO)-X(JP))**2 + (Y(JO)-Y(JP))**2)
C
C------ skip null panel
        IF(DSO .EQ. 0.0) GO TO 20
C
        DSIO = 1.0 / DSO
C
ccc     APAN = APANEL(JO)
        APAN = PI - APANEL(JO) + 2.0*ALFA
C
        XJO = X(JO) + 2.0*(YIMAGE+Y(JO))*SINA
        YJO = Y(JO) - 2.0*(YIMAGE+Y(JO))*COSA
        XJP = X(JP) + 2.0*(YIMAGE+Y(JP))*SINA
        YJP = Y(JP) - 2.0*(YIMAGE+Y(JP))*COSA
C
        RX1 = XI - XJO
        RY1 = YI - YJO
        RX2 = XI - XJP
        RY2 = YI - YJP
C
        SX = (XJP - XJO) * DSIO
        SY = (YJP - YJO) * DSIO
C
        X1 = SX*RX1 + SY*RY1
        X2 = SX*RX2 + SY*RY2
        YY = SX*RY1 - SY*RX1
C
        RS1 = RX1*RX1 + RY1*RY1
        RS2 = RX2*RX2 + RY2*RY2
C
C------ set reflection flag SGN to avoid branch problems with arctan
        IF(IO.GE.1 .AND. IO.LE.N) THEN
C------- no problem on airfoil surface
         SGN = 1.0
        ELSE
C------- make sure arctan falls between  -/+  Pi/2
         SGN = SIGN(1.0,YY)
        ENDIF
C
C------ set log(r^2) and arctan(x/y), correcting for reflection if any
        G1 = LOG(RS1)
        T1 = ATAN2(SGN*X1,SGN*YY) + (0.5 - 0.5*SGN)*PI
C
        G2 = LOG(RS2)
        T2 = ATAN2(SGN*X2,SGN*YY) + (0.5 - 0.5*SGN)*PI
C
        X1I = SX*NXI + SY*NYI
        X2I = SX*NXI + SY*NYI
        YYI = SX*NYI - SY*NXI
C
        IF(GEOLIN) THEN
         NXO = NX(JO)
         NYO = NY(JO)
         NXP = NX(JP)
         NYP = NY(JP)
C
         X1O =-((RX1-X1*SX)*NXO + (RY1-X1*SY)*NYO)*DSIO-(SX*NXO+SY*NYO)
         X1P = ((RX1-X1*SX)*NXP + (RY1-X1*SY)*NYP)*DSIO
         X2O =-((RX2-X2*SX)*NXO + (RY2-X2*SY)*NYO)*DSIO
         X2P = ((RX2-X2*SX)*NXP + (RY2-X2*SY)*NYP)*DSIO-(SX*NXP+SY*NYP)
         YYO = ((RX1+X1*SY)*NYO - (RY1-X1*SX)*NXO)*DSIO-(SX*NYO-SY*NXO)
         YYP =-((RX1-X1*SY)*NYP - (RY1+X1*SX)*NXP)*DSIO
        ENDIF
C
        IF(JO.EQ.N) GO TO 21
C
        IF(SIGLIN) THEN
C
C------- set up midpoint quantities
         X0 = 0.5*(X1+X2)
         RS0 = X0*X0 + YY*YY
         G0 = LOG(RS0)
         T0 = ATAN2(SGN*X0,SGN*YY) + (0.5 - 0.5*SGN)*PI
C
C------- calculate source contribution to Psi  for  1-0  half-panel
         DXINV = 1.0/(X1-X0)
         PSUM = X0*(T0-APAN) - X1*(T1-APAN) + 0.5*YY*(G1-G0)
         PDIF = ((X1+X0)*PSUM + RS1*(T1-APAN) - RS0*(T0-APAN)
     &        + (X0-X1)*YY) * DXINV
         PSUMTERM1 = X0*(T0-APAN)
         PSUMTERM2 = X1*(T1-APAN)
         PSUMTERM3 = 0.5*YY*(G1-G0)
         PSUMACCUM = PSUMTERM1 - PSUMTERM2
         PDIFTERM1 = (X1+X0)*PSUM
         PDIFTERM2 = RS1*(T1-APAN)
         PDIFTERM3 = RS0*(T0-APAN)
         PDIFTERM4 = (X0-X1)*YY
         PDIFBASE = PDIFTERM1 + PDIFTERM2
         PDIFACCUM = PDIFBASE - PDIFTERM3
         PDIFNUMERATOR = PDIFACCUM + PDIFTERM4
         CALL TRACE_PSWLIN_HALF_TERMS('PSWLIN', IO, -1, JO-N, 1,
     &        X0, PSUMTERM1, PSUMTERM2, PSUMTERM3, PSUMACCUM, PSUM,
     &        PDIFTERM1, PDIFTERM2, PDIFTERM3, PDIFTERM4,
     &        PDIFBASE, PDIFACCUM, PDIFNUMERATOR, PDIF)
         CALL TRACE_PSWLIN_HALF_TERMS('PSWLIN', IO, -1, JO-N, 1,
     &        X0,
     &        X0*(T0-APAN), X1*(T1-APAN), 0.5*YY*(G1-G0),
     &        X0*(T0-APAN) - X1*(T1-APAN), PSUM,
     &        (X1+X0)*PSUM, RS1*(T1-APAN), RS0*(T0-APAN),
     &        (X0-X1)*YY,
     &        (X1+X0)*PSUM + RS1*(T1-APAN),
     &        (X1+X0)*PSUM + RS1*(T1-APAN) - RS0*(T0-APAN),
     &        ((X1+X0)*PSUM + RS1*(T1-APAN) - RS0*(T0-APAN))
     &          + (X0-X1)*YY,
     &        PDIF)
C
         PSX1 =  -(T1-APAN)
         PSX0 =    T0-APAN
         PSYY =  0.5*(G1-G0)
C
         PDX1 = ((X1+X0)*PSX1 + PSUM + 2.0*X1*(T1-APAN) - PDIF) * DXINV
         PDX0TERM1 = (X1+X0)*PSX0
         PDX0TERM2 = PSUM
         PDX0TERM3 = 2.0*X0*(T0-APAN)
         PDX0ACCUM1 = PDX0TERM1 + PDX0TERM2
         PDX0ACCUM2 = PDX0ACCUM1 - PDX0TERM3
         PDX0NUMERATOR = PDX0ACCUM2 + PDIF
         PDX0 = PDX0NUMERATOR * DXINV
         PDYY = ((X1+X0)*PSYY + 2.0*(X0-X1 + YY*(T1-T0))      ) * DXINV
C
         DSM = SQRT((X(JP)-X(JM))**2 + (Y(JP)-Y(JM))**2)
         DSIM = 1.0/DSM
C
CCC      SIG0 = (SIG(JP) - SIG(JO))*DSIO
CCC      SIG1 = (SIG(JP) - SIG(JM))*DSIM
CCC      SSUM = SIG0 + SIG1
CCC      SDIF = SIG0 - SIG1
C
         SSUM = (SIG(JP) - SIG(JO))*DSIO + (SIG(JP) - SIG(JM))*DSIM
         SDIF = (SIG(JP) - SIG(JO))*DSIO - (SIG(JP) - SIG(JM))*DSIM
C
         PSI = PSI + QOPI*(PSUM*SSUM + PDIF*SDIF)
C
C------- dPsi/dm
         DZDM(JM) = DZDM(JM) + QOPI*(-PSUM*DSIM + PDIF*DSIM)
         DZDM(JO) = DZDM(JO) + QOPI*(-PSUM*DSIO - PDIF*DSIO)
         DZDM(JP) = DZDM(JP) + QOPI*( PSUM*(DSIO+DSIM)
     &                                          + PDIF*(DSIO-DSIM))
C
C------- dPsi/dni
         PSNI = PSX1*X1I + PSX0*(X1I+X2I)*0.5 + PSYY*YYI
         PDNI = PDX1*X1I + PDX0*(X1I+X2I)*0.5 + PDYY*YYI
         PSI_NI = PSI_NI + QOPI*(PSNI*SSUM + PDNI*SDIF)
C
         QTANM = QTANM + QOPI*(PSNI*SSUM + PDNI*SDIF)
C
         DQDM(JM) = DQDM(JM) + QOPI*(-PSNI*DSIM + PDNI*DSIM)
         DQDM(JO) = DQDM(JO) + QOPI*(-PSNI*DSIO - PDNI*DSIO)
         DQDM(JP) = DQDM(JP) + QOPI*( PSNI*(DSIO+DSIM)
     &                                          + PDNI*(DSIO-DSIM))
C
C
C------- calculate source contribution to Psi  for  0-2  half-panel
         DXINV = 1.0/(X0-X2)
         PSUM = X2*(T2-APAN) - X0*(T0-APAN) + 0.5*YY*(G0-G2)
         PDIF = ((X0+X2)*PSUM + RS0*(T0-APAN) - RS2*(T2-APAN)
     &        + (X2-X0)*YY) * DXINV
         PSUMTERM1 = X2*(T2-APAN)
         PSUMTERM2 = X0*(T0-APAN)
         PSUMTERM3 = 0.5*YY*(G0-G2)
         PSUMACCUM = PSUMTERM1 - PSUMTERM2
         PDIFTERM1 = (X0+X2)*PSUM
         PDIFTERM2 = RS0*(T0-APAN)
         PDIFTERM3 = RS2*(T2-APAN)
         PDIFTERM4 = (X2-X0)*YY
         PDIFBASE = PDIFTERM1 + PDIFTERM2
         PDIFACCUM = PDIFBASE - PDIFTERM3
         PDIFNUMERATOR = PDIFACCUM + PDIFTERM4
         CALL TRACE_PSWLIN_HALF_TERMS('PSWLIN', IO, -1, JO-N, 2,
     &        X0, PSUMTERM1, PSUMTERM2, PSUMTERM3, PSUMACCUM, PSUM,
     &        PDIFTERM1, PDIFTERM2, PDIFTERM3, PDIFTERM4,
     &        PDIFBASE, PDIFACCUM, PDIFNUMERATOR, PDIF)
         CALL TRACE_PSWLIN_HALF_TERMS('PSWLIN', IO, -1, JO-N, 2,
     &        X0,
     &        X2*(T2-APAN), X0*(T0-APAN), 0.5*YY*(G0-G2),
     &        X2*(T2-APAN) - X0*(T0-APAN), PSUM,
     &        (X0+X2)*PSUM, RS0*(T0-APAN), RS2*(T2-APAN),
     &        (X2-X0)*YY,
     &        (X0+X2)*PSUM + RS0*(T0-APAN),
     &        (X0+X2)*PSUM + RS0*(T0-APAN) - RS2*(T2-APAN),
     &        ((X0+X2)*PSUM + RS0*(T0-APAN) - RS2*(T2-APAN))
     &          + (X2-X0)*YY,
     &        PDIF)
C
         PSX0 =  -(T0-APAN)
         PSX2 =    T2-APAN
         PSYY =  0.5*(G0-G2)
C
         PDX0TERM1 = (X0+X2)*PSX0
         PDX0TERM2 = PSUM
         PDX0TERM3 = 2.0*X0*(T0-APAN)
         PDX0ACCUM1 = PDX0TERM1 + PDX0TERM2
         PDX0ACCUM2 = PDX0ACCUM1 + PDX0TERM3
         PDX0NUMERATOR = PDX0ACCUM2 - PDIF
         PDX0 = PDX0NUMERATOR * DXINV
         PDX2 = ((X0+X2)*PSX2 + PSUM - 2.0*X2*(T2-APAN) + PDIF) * DXINV
         PDYY = ((X0+X2)*PSYY + 2.0*(X2-X0 + YY*(T0-T2))      ) * DXINV
C
         DSP = SQRT((X(JQ)-X(JO))**2 + (Y(JQ)-Y(JO))**2)
         DSIP = 1.0/DSP
C
CCC         SIG2 = (SIG(JQ) - SIG(JO))*DSIP
CCC         SIG0 = (SIG(JP) - SIG(JO))*DSIO
CCC         SSUM = SIG2 + SIG0
CCC         SDIF = SIG2 - SIG0
C
         SSUM = (SIG(JQ) - SIG(JO))*DSIP + (SIG(JP) - SIG(JO))*DSIO
         SDIF = (SIG(JQ) - SIG(JO))*DSIP - (SIG(JP) - SIG(JO))*DSIO
C
         PSI = PSI + QOPI*(PSUM*SSUM + PDIF*SDIF)
C
C------- dPsi/dm
         DZDM(JO) = DZDM(JO) + QOPI*(-PSUM*(DSIP+DSIO)
     &                                          - PDIF*(DSIP-DSIO))
         DZDM(JP) = DZDM(JP) + QOPI*( PSUM*DSIO - PDIF*DSIO)
         DZDM(JQ) = DZDM(JQ) + QOPI*( PSUM*DSIP + PDIF*DSIP)
C
C------- dPsi/dni
         PSNI = PSX0*(X1I+X2I)*0.5 + PSX2*X2I + PSYY*YYI
         PDNI = PDX0*(X1I+X2I)*0.5 + PDX2*X2I + PDYY*YYI
         PSI_NI = PSI_NI + QOPI*(PSNI*SSUM + PDNI*SDIF)
C
         QTANM = QTANM + QOPI*(PSNI*SSUM + PDNI*SDIF)
C
         DQDM(JO) = DQDM(JO) + QOPI*(-PSNI*(DSIP+DSIO)
     &                                          - PDNI*(DSIP-DSIO))
         DQDM(JP) = DQDM(JP) + QOPI*( PSNI*DSIO - PDNI*DSIO)
         DQDM(JQ) = DQDM(JQ) + QOPI*( PSNI*DSIP + PDNI*DSIP)
C
        ENDIF
C
C------ calculate vortex panel contribution to Psi
        DXINV = 1.0/(X1-X2)
        PSIS = 0.5*X1*G1 - 0.5*X2*G2 + X2 - X1 + YY*(T1-T2)
        PSID = ((X1+X2)*PSIS + 0.5*(RS2*G2-RS1*G1 + X1*X1-X2*X2))*DXINV
C
        PSX1 = 0.5*G1
        PSX2 = -.5*G2
        PSYY = T1-T2
C
        PDX1 = ((X1+X2)*PSX1 + PSIS - X1*G1 - PSID)*DXINV
        PDX2 = ((X1+X2)*PSX2 + PSIS + X2*G2 + PSID)*DXINV
        PDYY = ((X1+X2)*PSYY - YY*(G1-G2)         )*DXINV
C
        GSUM1 = GAMU(JP,1) + GAMU(JO,1)
        GSUM2 = GAMU(JP,2) + GAMU(JO,2)
        GDIF1 = GAMU(JP,1) - GAMU(JO,1)
        GDIF2 = GAMU(JP,2) - GAMU(JO,2)
C
        GSUM = GAM(JP) + GAM(JO)
        GDIF = GAM(JP) - GAM(JO)
C
        PSI = PSI - QOPI*(PSIS*GSUM + PSID*GDIF)
C
C------ dPsi/dGam
        DZDG(JO) = DZDG(JO) - QOPI*(PSIS-PSID)
        DZDG(JP) = DZDG(JP) - QOPI*(PSIS+PSID)
C
C------ dPsi/dni
        PSNI = PSX1*X1I + PSX2*X2I + PSYY*YYI
        PDNI = PDX1*X1I + PDX2*X2I + PDYY*YYI
        PSI_NI = PSI_NI - QOPI*(GSUM*PSNI + GDIF*PDNI)
C
        QTAN1 = QTAN1 - QOPI*(GSUM1*PSNI + GDIF1*PDNI)
        QTAN2 = QTAN2 - QOPI*(GSUM2*PSNI + GDIF2*PDNI)
C
        DQDG(JO) = DQDG(JO) - QOPI*(PSNI - PDNI)
        DQDG(JP) = DQDG(JP) - QOPI*(PSNI + PDNI)
C
        IF(GEOLIN) THEN
C
C------- dPsi/dn
         DZDN(JO) = DZDN(JO)- QOPI*GSUM*(PSX1*X1O + PSX2*X2O + PSYY*YYO)
     &                      - QOPI*GDIF*(PDX1*X1O + PDX2*X2O + PDYY*YYO)
         DZDN(JP) = DZDN(JP)- QOPI*GSUM*(PSX1*X1P + PSX2*X2P + PSYY*YYP)
     &                      - QOPI*GDIF*(PDX1*X1P + PDX2*X2P + PDYY*YYP)
C------- dPsi/dP
         Z_QDOF0 = Z_QDOF0
     &           - QOPI*((PSIS-PSID)*QF0(JO) + (PSIS+PSID)*QF0(JP))
         Z_QDOF1 = Z_QDOF1
     &           - QOPI*((PSIS-PSID)*QF1(JO) + (PSIS+PSID)*QF1(JP))
         Z_QDOF2 = Z_QDOF2
     &           - QOPI*((PSIS-PSID)*QF2(JO) + (PSIS+PSID)*QF2(JP))
         Z_QDOF3 = Z_QDOF3
     &           - QOPI*((PSIS-PSID)*QF3(JO) + (PSIS+PSID)*QF3(JP))
        ENDIF
C
C
   20 CONTINUE
C
   21 CONTINUE
      PSIG = 0.5*YY*(G1-G2) + X2*(T2-APAN) - X1*(T1-APAN)
      PGAM = 0.5*X1*G1 - 0.5*X2*G2 + X2 - X1 + YY*(T1-T2)
C
      PSIGX1 = -(T1-APAN)
      PSIGX2 =   T2-APAN
      PSIGYY = 0.5*(G1-G2)
      PGAMX1 = 0.5*G1
      PGAMX2 = -.5*G2
      PGAMYY = T1-T2
C
      PSIGNI = PSIGX1*X1I + PSIGX2*X2I + PSIGYY*YYI
      PGAMNI = PGAMX1*X1I + PGAMX2*X2I + PGAMYY*YYI
C
C---- TE panel source and vortex strengths
      SIGTE1 = 0.5*SCS*(GAMU(JP,1) - GAMU(JO,1))
      SIGTE2 = 0.5*SCS*(GAMU(JP,2) - GAMU(JO,2))
      GAMTE1 = -.5*SDS*(GAMU(JP,1) - GAMU(JO,1))
      GAMTE2 = -.5*SDS*(GAMU(JP,2) - GAMU(JO,2))
C
      SIGTE = 0.5*SCS*(GAM(JP) - GAM(JO))
      GAMTE = -.5*SDS*(GAM(JP) - GAM(JO))
C
C---- TE panel contribution to Psi
      PSI = PSI + HOPI*(PSIG*SIGTE - PGAM*GAMTE)
C
C---- dPsi/dGam
      DZDG(JO) = DZDG(JO) - HOPI*PSIG*SCS*0.5
      DZDG(JP) = DZDG(JP) + HOPI*PSIG*SCS*0.5
C
      DZDG(JO) = DZDG(JO) - HOPI*PGAM*SDS*0.5
      DZDG(JP) = DZDG(JP) + HOPI*PGAM*SDS*0.5
C
C---- dPsi/dni
      PSI_NI = PSI_NI + HOPI*(PSIGNI*SIGTE - PGAMNI*GAMTE)
C
      QTAN1 = QTAN1 + HOPI*(PSIGNI*SIGTE1 - PGAMNI*GAMTE1)
      QTAN2 = QTAN2 + HOPI*(PSIGNI*SIGTE2 - PGAMNI*GAMTE2)
C
      DQDG(JO) = DQDG(JO) - HOPI*(PSIGNI*0.5*SCS + PGAMNI*0.5*SDS)
      DQDG(JP) = DQDG(JP) + HOPI*(PSIGNI*0.5*SCS + PGAMNI*0.5*SDS)
C
      IF(GEOLIN) THEN
C
C----- dPsi/dn
       DZDN(JO) = DZDN(JO)
     &          + HOPI*(PSIGX1*X1O + PSIGX2*X2O + PSIGYY*YYO)*SIGTE
     &          - HOPI*(PGAMX1*X1O + PGAMX2*X2O + PGAMYY*YYO)*GAMTE
       DZDN(JP) = DZDN(JP)
     &          + HOPI*(PSIGX1*X1P + PSIGX2*X2P + PSIGYY*YYP)*SIGTE
     &          - HOPI*(PGAMX1*X1P + PGAMX2*X2P + PGAMYY*YYP)*GAMTE
C
C----- dPsi/dP
       Z_QDOF0 = Z_QDOF0 + HOPI*PSIG*0.5*(QF0(JP)-QF0(JO))*SCS
     &                   + HOPI*PGAM*0.5*(QF0(JP)-QF0(JO))*SDS
       Z_QDOF1 = Z_QDOF1 + HOPI*PSIG*0.5*(QF1(JP)-QF1(JO))*SCS
     &                   + HOPI*PGAM*0.5*(QF1(JP)-QF1(JO))*SDS
       Z_QDOF2 = Z_QDOF2 + HOPI*PSIG*0.5*(QF2(JP)-QF2(JO))*SCS
     &                   + HOPI*PGAM*0.5*(QF2(JP)-QF2(JO))*SDS
       Z_QDOF3 = Z_QDOF3 + HOPI*PSIG*0.5*(QF3(JP)-QF3(JO))*SCS
     &                   + HOPI*PGAM*0.5*(QF3(JP)-QF3(JO))*SDS
C
      ENDIF
C
   22 CONTINUE
C
      CALL TRACE_PSILIN_RESULT('PSILIN', IO, PSI, PSI_NI)
      CALL TRACE_EXIT('PSILIN')
      RETURN
      END


      SUBROUTINE PSWLIN(I,XI,YI,NXI,NYI,PSI,PSI_NI)
C--------------------------------------------------------------------
C     Calculates current streamfunction Psi and tangential velocity
C     Qtan at panel node or wake node I due to freestream and wake
C     sources Sig.  Also calculates sensitivity vectors dPsi/dSig
C     (DZDM) and dQtan/dSig (DQDM).
C
C          Airfoil:  1   < I < N
C          Wake:     N+1 < I < N+NW
C--------------------------------------------------------------------
      INCLUDE 'XFOIL.INC'
      REAL NXI, NYI
      INTEGER IO, IOWAKE
      LOGICAL LDEBUG50
      REAL DZM1, DZM2
      REAL DZJM, DZJO, DZJP, DZJQ
      REAL DZJOLEFT, DZJORIGHT, DZJOINNER
      REAL DQJM, DQJO, DQJP, DQJQ
      REAL DQJOLEFT, DQJORIGHT, DQJOINNER
C
      IO = I
C
      CALL TRACE_ENTER('PSWLIN')
      COSA = COS(ALFA)
      SINA = SIN(ALFA)
      INQUIRE(UNIT=50, OPENED=LDEBUG50)
      IOWAKE = -1
      IF(IO.GE.N+1 .AND. IO.LE.N+NW) IOWAKE = IO - N
      CALL TRACE_PSWLIN_FIELD('PSWLIN', IO, IOWAKE, XI, YI, NXI, NYI)
C
      DO 4 JO=N+1, N+NW
        DZDM(JO) = 0.0
        DQDM(JO) = 0.0
    4 CONTINUE
C
      PSI    = 0.
      PSI_NI = 0.
C
      DO 20 JO=N+1, N+NW-1
C
        JP = JO+1
C
        JM = JO-1
        JQ = JP+1
        IF(JO.EQ.N+1) THEN
         JM = JO
        ELSE IF(JO.EQ.N+NW-1) THEN
         JQ = JP
        ENDIF
C
        DSO = SQRT((X(JO)-X(JP))**2 + (Y(JO)-Y(JP))**2)
        DSIO = 1.0 / DSO
C
        APAN = APANEL(JO)
C
        RX1 = XI - X(JO)
        RY1 = YI - Y(JO)
        RX2 = XI - X(JP)
        RY2 = YI - Y(JP)
C
        SX = (X(JP) - X(JO)) * DSIO
        SY = (Y(JP) - Y(JO)) * DSIO
C
        X1 = SX*RX1 + SY*RY1
        X2 = SX*RX2 + SY*RY2
        YY = SX*RY1 - SY*RX1
C
        RS1 = RX1*RX1 + RY1*RY1
        RS2 = RX2*RX2 + RY2*RY2
C
        IF(IO.GE.N+1 .AND. IO.LE.N+NW) THEN
         SGN = 1.0
        ELSE
         SGN = SIGN(1.0,YY)
        ENDIF
        CALL TRACE_PSWLIN_GEOMETRY('PSWLIN', IO, IOWAKE, JO-N,
     &       X(JO), Y(JO), X(JP), Y(JP),
     &       X(JP)-X(JO), Y(JP)-Y(JO), DSO, DSIO,
     &       SX, SY, RX1, RY1, RX2, RY2)
C
        IF(IO.EQ.78 .AND. JO.EQ.N+2) THEN
         WRITE(0,'(A,A,Z8,A,Z8,A,Z8,A,Z8,A,Z8,A,Z8,A,Z8,
     &    A,Z8,A,Z8,A,Z8,A,Z8,A,Z8,A,Z8)')
     &    'F_PSWLIN77','  SX=',TRANSFER(SX,1),
     &    ' SY=',TRANSFER(SY,1),
     &    ' DX=',TRANSFER(X(JP)-X(JO),1),
     &    ' DY=',TRANSFER(Y(JP)-Y(JO),1),
     &    ' DSO=',TRANSFER(DSO,1),
     &    ' DSIO=',TRANSFER(DSIO,1),
     &    ' X1=',TRANSFER(X1,1),
     &    ' X2=',TRANSFER(X2,1),
     &    ' YY=',TRANSFER(YY,1),
     &    ' RS1=',TRANSFER(RS1,1),
     &    ' RS2=',TRANSFER(RS2,1),
     &    ' XJo=',TRANSFER(X(JO),1),
     &    ' YJo=',TRANSFER(Y(JO),1)
        ENDIF
        IF(IO.NE.JO .AND. RS1.GT.0.0) THEN
         G1 = LOG(RS1)
         T1 = ATAN2(SGN*X1,SGN*YY) - (0.5 - 0.5*SGN)*PI
        ELSE
         G1 = 0.0
         T1 = 0.0
        ENDIF
C
        IF(IO.NE.JP .AND. RS2.GT.0.0) THEN
         G2 = LOG(RS2)
         T2 = ATAN2(SGN*X2,SGN*YY) - (0.5 - 0.5*SGN)*PI
        ELSE
         G2 = 0.0
         T2 = 0.0
        ENDIF
        IF(IO.EQ.78 .AND. JO.EQ.N+2) THEN
         WRITE(0,'(A,A,Z8,A,Z8,A,Z8,A,Z8,A,Z8)')
     &    'F_PSWLIN77_GT',
     &    ' G1=',TRANSFER(G1,1),
     &    ' T1=',TRANSFER(T1,1),
     &    ' G2=',TRANSFER(G2,1),
     &    ' T2=',TRANSFER(T2,1),
     &    ' SGN=',TRANSFER(SGN,1)
        ENDIF
C
        X1I = SX*NXI + SY*NYI
        X2I = SX*NXI + SY*NYI
        YYI = SX*NYI - SY*NXI
C
C------- set up midpoint quantities
         X0 = 0.5*(X1+X2)
         RS0 = X0*X0 + YY*YY
         G0 = LOG(RS0)
         T0 = ATAN2(SGN*X0,SGN*YY) - (0.5 - 0.5*SGN)*PI
C
C------- calculate source contribution to Psi  for  1-0  half-panel
         DXINV = 1.0/(X1-X0)
         PSUM = X0*(T0-APAN) - X1*(T1-APAN) + 0.5*YY*(G1-G0)
         PDIF = ((X1+X0)*PSUM + RS1*(T1-APAN) - RS0*(T0-APAN)
     &        + (X0-X1)*YY) * DXINV
         PSUMTERM1 = X0*(T0-APAN)
         PSUMTERM2 = X1*(T1-APAN)
         PSUMTERM3 = 0.5*YY*(G1-G0)
         PSUMACCUM = PSUMTERM1 - PSUMTERM2
         PDIFTERM1 = (X1+X0)*PSUM
         PDIFTERM2 = RS1*(T1-APAN)
         PDIFTERM3 = RS0*(T0-APAN)
         PDIFTERM4 = (X0-X1)*YY
         PDIFBASE = PDIFTERM1 + PDIFTERM2
         PDIFACCUM = PDIFBASE - PDIFTERM3
         PDIFNUMERATOR = PDIFACCUM + PDIFTERM4
         CALL TRACE_PSWLIN_HALF_TERMS('PSWLIN', IO, -1, JO-N, 1,
     &        X0, PSUMTERM1, PSUMTERM2, PSUMTERM3, PSUMACCUM, PSUM,
     &        PDIFTERM1, PDIFTERM2, PDIFTERM3, PDIFTERM4,
     &        PDIFBASE, PDIFACCUM, PDIFNUMERATOR, PDIF)
C
         PSX1 =  -(T1-APAN)
         PSX0 =    T0-APAN
         PSYY =  0.5*(G1-G0)
C
         PDX1 = ((X1+X0)*PSX1 + PSUM + 2.0*X1*(T1-APAN) - PDIF) * DXINV
         CALL TRACE_PSWLIN_PDX1_TERMS('PSWLIN', IO, -1, JO-N, 1,
     &        (X1+X0)*PSX1, PSUM, 2.0*X1*(T1-APAN),
     &        (X1+X0)*PSX1 + PSUM,
     &        ((X1+X0)*PSX1 + PSUM) + 2.0*X1*(T1-APAN),
     &        ((X1+X0)*PSX1 + PSUM) + 2.0*X1*(T1-APAN) - PDIF,
     &        PDX1)
         PDX0 = ((X1+X0)*PSX0 + PSUM - 2.0*X0*(T0-APAN) + PDIF) * DXINV
         CALL TRACE_PSWLIN_PDX0_TERMS('PSWLIN', IO, -1, JO-N, 1,
     &        (X1+X0)*PSX0, PSUM, 2.0*X0*(T0-APAN),
     &        (X1+X0)*PSX0 + PSUM,
     &        ((X1+X0)*PSX0 + PSUM) - 2.0*X0*(T0-APAN),
     &        ((X1+X0)*PSX0 + PSUM) - 2.0*X0*(T0-APAN) + PDIF,
     &        PDX0)
         PDYY = ((X1+X0)*PSYY + 2.0*(X0-X1 + YY*(T1-T0))      ) * DXINV
C
         DSM = SQRT((X(JP)-X(JM))**2 + (Y(JP)-Y(JM))**2)
         DSIM = 1.0/DSM
C
CCC         SIG0 = (SIG(JP) - SIG(JO))*DSIO
CCC         SIG1 = (SIG(JP) - SIG(JM))*DSIM
CCC         SSUM = SIG0 + SIG1
CCC         SDIF = SIG0 - SIG1
C
         SSUM = (SIG(JP) - SIG(JO))*DSIO + (SIG(JP) - SIG(JM))*DSIM
         SDIF = (SIG(JP) - SIG(JO))*DSIO - (SIG(JP) - SIG(JM))*DSIM
C
         PSI = PSI + QOPI*(PSUM*SSUM + PDIF*SDIF)
C
C------- dPsi/dm
         DZM1 = 0.0
         IF(JM.EQ.N+1) DZM1 = DZM1 + QOPI*(-PSUM*DSIM + PDIF*DSIM)
         IF(JO.EQ.N+1) DZM1 = DZM1 + QOPI*(-PSUM*DSIO - PDIF*DSIO)
         IF(JP.EQ.N+1) DZM1 = DZM1 + QOPI*( PSUM*(DSIO+DSIM)
     &                                          + PDIF*(DSIO-DSIM))
         DZJM = QOPI*(-PSUM*DSIM + PDIF*DSIM)
         DZJOLEFT = -PSUM*DSIO
         DZJORIGHT = PDIF*DSIO
         DZJOINNER = DZJOLEFT - DZJORIGHT
         DZJO = QOPI*DZJOINNER
         DZJP = QOPI*( PSUM*(DSIO+DSIM) + PDIF*(DSIO-DSIM))
         DZJQ = 0.0
         DZDM(JM) = DZDM(JM) + DZJM
         IF(JM.EQ.N+1) CALL TRACE_WAKE_SOURCE_ACCUM('PSWLIN', IO,
     &        IOWAKE, JO-N, 1, 1, 'dzdm', 'jm', DZJM, DZDM(JM))
         DZDM(JO) = DZDM(JO) + DZJO
         IF(JO.EQ.N+1) CALL TRACE_WAKE_SOURCE_ACCUM('PSWLIN', IO,
     &        IOWAKE, JO-N, 1, 1, 'dzdm', 'jo', DZJO, DZDM(JO))
         DZDM(JP) = DZDM(JP) + DZJP
         IF(JP.EQ.N+1) CALL TRACE_WAKE_SOURCE_ACCUM('PSWLIN', IO,
     &        IOWAKE, JO-N, 1, 1, 'dzdm', 'jp', DZJP, DZDM(JP))
         IF(LDEBUG50 .AND. (IO.EQ.74 .OR. IO.EQ.80)
     &      .AND. (JO.EQ.N+1 .OR. JO.EQ.N+2)) THEN
          WRITE(50,9911) IO, JO-N, 1, X1, X2, YY, SGN, APAN
          WRITE(50,9912) IO, JO-N, T0, T1, T2, PSUM, PDIF, DZM1
         ENDIF
C
C------- dPsi/dni
         XSUMNI = X1I + X2I
         XHALFNI = XSUMNI*0.5
         PSLRAW = PSX0*XSUMNI
         PSLSCL = PSLRAW*0.5
         PSTERM1 = PSX1*X1I
         PSTERM2 = PSLSCL
         PSTERM3 = PSYY*YYI
         PSACC12 = PSTERM1 + PSTERM2
         PDLRAW = PDX0*XSUMNI
         PDLSCL = PDLRAW*0.5
         PDTERM1 = PDX1*X1I
         PDTERM2 = PDLSCL
         PDTERM3 = PDYY*YYI
         PDACC12 = PDTERM1 + PDTERM2
         PSNI = PSX1*X1I + PSX0*(X1I+X2I)*0.5 + PSYY*YYI
         PDNI = PDX1*X1I + PDX0*(X1I+X2I)*0.5 + PDYY*YYI
         CALL TRACE_PSWLIN_NI_TERMS('PSWLIN', IO, IOWAKE, JO-N, 1,
     &        XSUMNI, XHALFNI,
     &        PSLRAW, PSLSCL, PSTERM1, PSTERM2, PSTERM3,
     &        PSACC12, PSNI,
     &        PDLRAW, PDLSCL, PDTERM1, PDTERM2, PDTERM3,
     &        PDACC12, PDNI)
         PSI_NI = PSI_NI + QOPI*(PSNI*SSUM + PDNI*SDIF)
C
         DQJM = QOPI*(-PSNI*DSIM + PDNI*DSIM)
         DQJOLEFT = -PSNI*DSIO
         DQJORIGHT = PDNI*DSIO
         DQJOINNER = DQJOLEFT - DQJORIGHT
         DQJO = QOPI*DQJOINNER
         DQJP = QOPI*( PSNI*(DSIO+DSIM) + PDNI*(DSIO-DSIM))
         DQJQ = 0.0
         DQDM(JM) = DQDM(JM) + DQJM
         DQDM(JO) = DQDM(JO) + DQJO
         DQDM(JP) = DQDM(JP) + DQJP
         IF(IOWAKE.EQ.5.AND.(JM.EQ.N+5.OR.JO.EQ.N+5.OR.JP.EQ.N+5))
     &    WRITE(*,'(A,I3,A,Z8)')
     &     'F_PSWQ5 seg=',JO-N,' dq5=',TRANSFER(DQDM(N+5),1)
         CALL TRACE_PSWLIN_RECURRENCE('PSWLIN', IO, IOWAKE, JO-N, 1,
     &        DZJOLEFT, DZJORIGHT, DZJOINNER, DZJO,
     &        DQJOLEFT, DQJORIGHT, DQJOINNER, DQJO, QOPI)
         CALL TRACE_PSWLIN_SEGMENT('PSWLIN', IO, IOWAKE,
     &        1, JM-N, JO-N, JP-N, JQ-N,
     &        X1, X2, YY, SGN, APAN,
     &        X1I, X2I, YYI,
     &        RS0, RS1, RS2, G0, G1, G2, T0, T1, T2,
     &        DSO, DSIO, DSM, DSIM, 0.0, 0.0, DXINV,
     &        SSUM, SDIF, PSUM, PDIF,
     &        PSX0, PSX1, 0.0, PSYY,
     &        PDX0, PDX1, 0.0, PDYY,
     &        PSNI, PDNI,
     &        DZJM, DZJO, DZJP, DZJQ,
     &        DQJM, DQJO, DQJP, DQJQ)
C
C
C------- calculate source contribution to Psi  for  0-2  half-panel
         DXINV = 1.0/(X0-X2)
         PSUM = X2*(T2-APAN) - X0*(T0-APAN) + 0.5*YY*(G0-G2)
         PDIF = ((X0+X2)*PSUM + RS0*(T0-APAN) - RS2*(T2-APAN)
     &        + (X2-X0)*YY) * DXINV
         PSUMTERM1 = X2*(T2-APAN)
         PSUMTERM2 = X0*(T0-APAN)
         PSUMTERM3 = 0.5*YY*(G0-G2)
         PSUMACCUM = PSUMTERM1 - PSUMTERM2
         PDIFTERM1 = (X0+X2)*PSUM
         PDIFTERM2 = RS0*(T0-APAN)
         PDIFTERM3 = RS2*(T2-APAN)
         PDIFTERM4 = (X2-X0)*YY
         PDIFBASE = PDIFTERM1 + PDIFTERM2
         PDIFACCUM = PDIFBASE - PDIFTERM3
         PDIFNUMERATOR = PDIFACCUM + PDIFTERM4
         CALL TRACE_PSWLIN_HALF_TERMS('PSWLIN', IO, -1, JO-N, 2,
     &        X0, PSUMTERM1, PSUMTERM2, PSUMTERM3, PSUMACCUM, PSUM,
     &        PDIFTERM1, PDIFTERM2, PDIFTERM3, PDIFTERM4,
     &        PDIFBASE, PDIFACCUM, PDIFNUMERATOR, PDIF)
C
         PSX0 =  -(T0-APAN)
         PSX2 =    T2-APAN
         PSYY =  0.5*(G0-G2)
C
         PDX0 = ((X0+X2)*PSX0 + PSUM + 2.0*X0*(T0-APAN) - PDIF) * DXINV
         CALL TRACE_PSWLIN_PDX0_TERMS('PSWLIN', IO, -1, JO-N, 2,
     &        (X0+X2)*PSX0, PSUM, 2.0*X0*(T0-APAN),
     &        (X0+X2)*PSX0 + PSUM,
     &        ((X0+X2)*PSX0 + PSUM) + 2.0*X0*(T0-APAN),
     &        ((X0+X2)*PSX0 + PSUM) + 2.0*X0*(T0-APAN) - PDIF,
     &        PDX0)
         PDX2 = ((X0+X2)*PSX2 + PSUM - 2.0*X2*(T2-APAN) + PDIF) * DXINV
         CALL TRACE_PSWLIN_PDX2_TERMS('PSWLIN', IO, -1, JO-N, 2,
     &        (X0+X2)*PSX2, PSUM, 2.0*X2*(T2-APAN),
     &        (X0+X2)*PSX2 + PSUM,
     &        ((X0+X2)*PSX2 + PSUM) - 2.0*X2*(T2-APAN),
     &        (((X0+X2)*PSX2 + PSUM) - 2.0*X2*(T2-APAN)) + PDIF,
     &        PDX2)
         PDYY = ((X0+X2)*PSYY + 2.0*(X2-X0 + YY*(T0-T2))      ) * DXINV
C
         DSP = SQRT((X(JQ)-X(JO))**2 + (Y(JQ)-Y(JO))**2)
         DSIP = 1.0/DSP
C
CCC         SIG2 = (SIG(JQ) - SIG(JO))*DSIP
CCC         SIG0 = (SIG(JP) - SIG(JO))*DSIO
CCC         SSUM = SIG2 + SIG0
CCC         SDIF = SIG2 - SIG0
C
         SSUM = (SIG(JQ) - SIG(JO))*DSIP + (SIG(JP) - SIG(JO))*DSIO
         SDIF = (SIG(JQ) - SIG(JO))*DSIP - (SIG(JP) - SIG(JO))*DSIO
C
         PSI = PSI + QOPI*(PSUM*SSUM + PDIF*SDIF)
C
C------- dPsi/dm
         DZM2 = 0.0
         IF(JO.EQ.N+1) DZM2 = DZM2 + QOPI*(-PSUM*(DSIP+DSIO)
     &                                          - PDIF*(DSIP-DSIO))
         IF(JP.EQ.N+1) DZM2 = DZM2 + QOPI*( PSUM*DSIO - PDIF*DSIO)
         IF(JQ.EQ.N+1) DZM2 = DZM2 + QOPI*( PSUM*DSIP + PDIF*DSIP)
         DZJM = 0.0
         DZJOLEFT = -PSUM*(DSIP+DSIO)
         DZJORIGHT = PDIF*(DSIP-DSIO)
         DZJOINNER = DZJOLEFT - DZJORIGHT
         DZJO = QOPI*DZJOINNER
         DZJP = QOPI*( PSUM*DSIO - PDIF*DSIO)
         DZJQ = QOPI*( PSUM*DSIP + PDIF*DSIP)
         DZDM(JO) = DZDM(JO) + DZJO
         IF(JO.EQ.N+1) CALL TRACE_WAKE_SOURCE_ACCUM('PSWLIN', IO,
     &        IOWAKE, JO-N, 2, 1, 'dzdm', 'jo', DZJO, DZDM(JO))
         DZDM(JP) = DZDM(JP) + DZJP
         IF(JP.EQ.N+1) CALL TRACE_WAKE_SOURCE_ACCUM('PSWLIN', IO,
     &        IOWAKE, JO-N, 2, 1, 'dzdm', 'jp', DZJP, DZDM(JP))
         DZDM(JQ) = DZDM(JQ) + DZJQ
         IF(JQ.EQ.N+1) CALL TRACE_WAKE_SOURCE_ACCUM('PSWLIN', IO,
     &        IOWAKE, JO-N, 2, 1, 'dzdm', 'jq', DZJQ, DZDM(JQ))
         IF(LDEBUG50 .AND. (IO.EQ.74 .OR. IO.EQ.80)
     &      .AND. (JO.EQ.N+1 .OR. JO.EQ.N+2)) THEN
          WRITE(50,9911) IO, JO-N, 2, X1, X2, YY, SGN, APAN
          WRITE(50,9912) IO, JO-N, T0, T1, T2, PSUM, PDIF, DZM2
         ENDIF
C
C------- dPsi/dni
         XSUMNI = X1I + X2I
         XHALFNI = XSUMNI*0.5
         PSLRAW = PSX0*XSUMNI
         PSLSCL = PSLRAW*0.5
         PSTERM1 = PSLSCL
         PSTERM2 = PSX2*X2I
         PSTERM3 = PSYY*YYI
         PSACC12 = PSTERM1 + PSTERM2
         PDLRAW = PDX0*XSUMNI
         PDLSCL = PDLRAW*0.5
         PDTERM1 = PDLSCL
         PDTERM2 = PDX2*X2I
         PDTERM3 = PDYY*YYI
         PDACC12 = PDTERM1 + PDTERM2
         PSNI = PSX0*(X1I+X2I)*0.5 + PSX2*X2I + PSYY*YYI
         PDNI = PDX0*(X1I+X2I)*0.5 + PDX2*X2I + PDYY*YYI
         CALL TRACE_PSWLIN_NI_TERMS('PSWLIN', IO, IOWAKE, JO-N, 2,
     &        XSUMNI, XHALFNI,
     &        PSLRAW, PSLSCL, PSTERM1, PSTERM2, PSTERM3,
     &        PSACC12, PSNI,
     &        PDLRAW, PDLSCL, PDTERM1, PDTERM2, PDTERM3,
     &        PDACC12, PDNI)
         PSI_NI = PSI_NI + QOPI*(PSNI*SSUM + PDNI*SDIF)
C
         DQJM = 0.0
         DQJOLEFT = -PSNI*(DSIP+DSIO)
         DQJORIGHT = PDNI*(DSIP-DSIO)
         DQJOINNER = DQJOLEFT - DQJORIGHT
         DQJO = QOPI*DQJOINNER
         DQJP = QOPI*( PSNI*DSIO - PDNI*DSIO)
         DQJQ = QOPI*( PSNI*DSIP + PDNI*DSIP)
         DQDM(JO) = DQDM(JO) + DQJO
         DQDM(JP) = DQDM(JP) + DQJP
         DQDM(JQ) = DQDM(JQ) + DQJQ
         IF(IOWAKE.EQ.5.AND.(JO.EQ.N+5.OR.JP.EQ.N+5.OR.JQ.EQ.N+5))
     &    WRITE(*,'(A,I3,A,Z8)')
     &     'F_PSWQ5h2 seg=',JO-N,' dq5=',TRANSFER(DQDM(N+5),1)
         CALL TRACE_PSWLIN_RECURRENCE('PSWLIN', IO, IOWAKE, JO-N, 2,
     &        DZJOLEFT, DZJORIGHT, DZJOINNER, DZJO,
     &        DQJOLEFT, DQJORIGHT, DQJOINNER, DQJO, QOPI)
         CALL TRACE_PSWLIN_SEGMENT('PSWLIN', IO, IOWAKE,
     &        2, JM-N, JO-N, JP-N, JQ-N,
     &        X1, X2, YY, SGN, APAN,
     &        X1I, X2I, YYI,
     &        RS0, RS1, RS2, G0, G1, G2, T0, T1, T2,
     &        DSO, DSIO, 0.0, 0.0, DSP, DSIP, DXINV,
     &        SSUM, SDIF, PSUM, PDIF,
     &        PSX0, 0.0, PSX2, PSYY,
     &        PDX0, 0.0, PDX2, PDYY,
     &        PSNI, PDNI,
     &        DZJM, DZJO, DZJP, DZJQ,
     &        DQJM, DQJO, DQJP, DQJQ)
C
   20 CONTINUE
C
      DO 30 JO=N+1, N+NW
        CALL TRACE_WAKE_SOURCE_ENTRY('PSWLIN', IO, IOWAKE, JO-N,
     &                               DZDM(JO), DQDM(JO))
   30 CONTINUE
      CALL TRACE_EXIT('PSWLIN')
      RETURN
9911  FORMAT('PSWLIN_G I=',I4,' JO=',I2,' H=',I1,' X1=',1PE15.8,
     &       ' X2=',1PE15.8,' YY=',1PE15.8,' SGN=',1PE15.8,
     &       ' AP=',1PE15.8)
9912  FORMAT('PSWLIN_C I=',I4,' JO=',I2,' T0=',1PE15.8,' T1=',
     &       1PE15.8,' T2=',1PE15.8,' PS=',1PE15.8,' PD=',1PE15.8,
     &       ' DZ1=',1PE15.8)
      END




      SUBROUTINE GGCALC
C--------------------------------------------------------------
C     Calculates two surface vorticity (gamma) distributions
C     for alpha = 0, 90  degrees.  These are superimposed
C     in SPECAL or SPECCL for specified alpha or CL.
C--------------------------------------------------------------
      INCLUDE 'XFOIL.INC'
      LOGICAL LDEBUG50
      CHARACTER*64 LU_TRACE_CONTEXT, BAKSUB_TRACE_CONTEXT
      COMMON /TRACE_LU_CTX/ LU_TRACE_CONTEXT
      COMMON /TRACE_BAKSUB_CTX/ BAKSUB_TRACE_CONTEXT
C
C---- distance of internal control point ahead of sharp TE
C-    (fraction of smaller panel length adjacent to TE)
      BWT = 0.1
C
      WRITE(*,*) 'Calculating unit vorticity distributions ...'
      INQUIRE(UNIT=50, OPENED=LDEBUG50)
      CALL TRACE_ENTER('GGCALC')
C
C---- GDB parity: dump panel coordinates and angles at sample indices
      IF(N.GE.80) THEN
        DO 11 IPDX=1, N, MAX(1,N/10)
         WRITE(0,'(A,I4,A,Z8,A,Z8,A,Z8)')
     &    'F_PAN_XY i=',IPDX,
     &    ' X=',TRANSFER(X(IPDX),1),
     &    ' Y=',TRANSFER(Y(IPDX),1),
     &    ' A=',TRANSFER(APANEL(IPDX),1)
   11   CONTINUE
        WRITE(0,'(A,I4,A,Z8,A,Z8,A,Z8)')
     &   'F_PAN_XY i=',N,
     &   ' X=',TRANSFER(X(N),1),
     &   ' Y=',TRANSFER(Y(N),1),
     &   ' A=',TRANSFER(APANEL(N),1)
        IAHASH = 0
        DO 12 IPDX=1, N
         IAHASH = IEOR(IAHASH, TRANSFER(APANEL(IPDX),1))
   12   CONTINUE
        WRITE(0,'(A,Z8)') 'F_PAN_AHASH=',IAHASH
        DO 13 IPDX=1, N
         WRITE(0,'(A,I4,A,Z8)')
     &    'F_PAN_ANG i=',IPDX,
     &    ' A=',TRANSFER(APANEL(IPDX),1)
   13   CONTINUE
        DO 14 IPDX=1, N
         WRITE(0,'(A,I4,A,Z8,A,Z8)')
     &    'F_PAN_FXY i=',IPDX,
     &    ' X=',TRANSFER(X(IPDX),1),
     &    ' Y=',TRANSFER(Y(IPDX),1)
   14   CONTINUE
        DO 15 IPDX=1, NB
         WRITE(0,'(A,I4,A,Z8,A,Z8)')
     &    'F_BUF_XY i=',IPDX,
     &    ' X=',TRANSFER(XB(IPDX),1),
     &    ' Y=',TRANSFER(YB(IPDX),1)
   15   CONTINUE
      ENDIF
C
      DO 10 I=1, N
        GAM(I) = 0.
        GAMU(I,1) = 0.
        GAMU(I,2) = 0.
   10 CONTINUE
      PSIO = 0.
C
C---- Set up matrix system for  Psi = Psio  on airfoil surface.
C-    The unknowns are (dGamma)i and dPsio.
      DO 20 I=1, N
C
C------ calculate Psi and dPsi/dGamma array for current node
        CALL PSILIN(I,X(I),Y(I),NX(I),NY(I),PSI,PSI_N,.FALSE.,.TRUE.)
C
        PSIINF = QINF*(COS(ALFA)*Y(I) - SIN(ALFA)*X(I))
C
C------ RES1 = PSI( 0) - PSIO
C------ RES2 = PSI(90) - PSIO
        RES1 =  QINF*Y(I)
        RES2 = -QINF*X(I)
C
C------ dRes/dGamma
        DO 201 J=1, N
          AIJ(I,J) = DZDG(J)
  201   CONTINUE
C
        DO 202 J=1, N
          BIJ(I,J) = -DZDM(J)
  202   CONTINUE
C
C------ dRes/dPsio
        AIJ(I,N+1) = -1.0
C
        GAMU(I,1) = -RES1
        GAMU(I,2) = -RES2
C
   20 CONTINUE
C
C---- set Kutta condition
C-    RES = GAM(1) + GAM(N)
      RES = 0.
C
      DO 30 J=1, N+1
        AIJ(N+1,J) = 0.0
   30 CONTINUE
C
      AIJ(N+1,1) = 1.0
      AIJ(N+1,N) = 1.0
C
      GAMU(N+1,1) = -RES
      GAMU(N+1,2) = -RES
C
C---- set up Kutta condition (no direct source influence)
      DO 32 J=1, N
        BIJ(N+1,J) = 0.
   32 CONTINUE
C
      IF(SHARP) THEN
C----- set zero internal velocity in TE corner 
C
C----- set TE bisector angle
       AG1 = ATAN2(-YP(1),-XP(1)    )
       AG2 = ATANC( YP(N), XP(N),AG1)
       ABIS = 0.5*(AG1+AG2)
       CBIS = COS(ABIS)
       SBIS = SIN(ABIS)
C
C----- minimum panel length adjacent to TE
       DS1 = SQRT( (X(1)-X(2)  )**2 + (Y(1)-Y(2)  )**2 )
       DS2 = SQRT( (X(N)-X(N-1))**2 + (Y(N)-Y(N-1))**2 )
       DSMIN = MIN( DS1 , DS2 )
C
C----- control point on bisector just ahead of TE point
       XBIS = XTE - BWT*DSMIN*CBIS
       YBIS = YTE - BWT*DSMIN*SBIS
ccc       write(*,*) xbis, ybis
C
C----- set velocity component along bisector line
       CALL PSILIN(0,XBIS,YBIS,-SBIS,CBIS,PSI,QBIS,.FALSE.,.TRUE.)
C
CCC--- RES = DQDGj*Gammaj + DQDMj*Massj + QINF*(COSA*CBIS + SINA*SBIS)
       RES = QBIS
C
C----- dRes/dGamma
       DO J=1, N
         AIJ(N,J) = DQDG(J)
       ENDDO
C
C----- -dRes/dMass
       DO J=1, N
         BIJ(N,J) = -DQDM(J)
       ENDDO
C
C----- dRes/dPsio
       AIJ(N,N+1) = 0.
C
C----- -dRes/dUinf
       GAMU(N,1) = -CBIS
C
C----- -dRes/dVinf
       GAMU(N,2) = -SBIS
C
      ENDIF
C
      IF(LDEBUG50) THEN
       DO 35 I=1, N+1
        DO 36 J=1, N+1
          WRITE(50,9907) I, J, AIJ(I,J)
   36   CONTINUE
   35  CONTINUE
      ENDIF
C
      DO 37 I=1, N+1
        CALL TRACE_BASIS_ENTRY('GGCALC', 'basis_rhs_alpha0',
     &                         I, GAMU(I,1))
        CALL TRACE_BASIS_ENTRY('GGCALC', 'basis_rhs_alpha90',
     &                         I, GAMU(I,2))
        DO 38 J=1, N+1
          CALL TRACE_MATRIX_ENTRY('GGCALC', 'aij', I, J, AIJ(I,J))
   38   CONTINUE
        DO 39 J=1, N
          CALL TRACE_MATRIX_ENTRY('GGCALC', 'bij', I, J, BIJ(I,J))
   39   CONTINUE
   37 CONTINUE
C
C---- GDB: dump AIJ row 33 before LU factorization
      WRITE(0,'(A,Z8,A,Z8,A,Z8,A,Z8)')
     &  'F_AIJ33 c1=',TRANSFER(AIJ(33,1),1),
     &  ' c2=',TRANSFER(AIJ(33,2),1),
     &  ' c3=',TRANSFER(AIJ(33,3),1),
     &  ' c80=',TRANSFER(AIJ(33,80),1)
C---- LU-factor coefficient matrix AIJ
      LU_TRACE_CONTEXT = 'basis_aij_single'
      CALL LUDCMP(IQX,N+1,AIJ,AIJPIV)
      LU_TRACE_CONTEXT = ' '
      LQAIJ = .TRUE.
C---- GDB: dump FULL LU matrix to binary file
      OPEN(UNIT=77,FILE='f_lu_matrix.bin',
     &     FORM='UNFORMATTED',ACCESS='STREAM')
      DO 9282 JJCOL=1, N+1
        DO 9283 JJROW=1, N+1
          WRITE(77) AIJ(JJROW,JJCOL)
 9283   CONTINUE
 9282 CONTINUE
      CLOSE(77)
      WRITE(0,'(A,I6,A)') 'F_LU_DUMP ',
     &  (N+1)*(N+1),' entries to f_lu_matrix.bin'
C
      DO 43 I=1, N+1
        CALL TRACE_PIVOT_ENTRY('GGCALC', 'basis_lu_pivot', I, AIJPIV(I))
        DO 44 J=1, N+1
          CALL TRACE_MATRIX_ENTRY('GGCALC', 'basis_lu_aij', I, J,
     &                            AIJ(I,J))
   44   CONTINUE
   43 CONTINUE
C
C---- solve system for the two vorticity distributions
      BAKSUB_TRACE_CONTEXT = 'basis_gamma_alpha0_single'
      CALL BAKSUB(IQX,N+1,AIJ,AIJPIV,GAMU(1,1))
      BAKSUB_TRACE_CONTEXT = 'basis_gamma_alpha90_single'
      CALL BAKSUB(IQX,N+1,AIJ,AIJPIV,GAMU(1,2))
      BAKSUB_TRACE_CONTEXT = ' '
C---- GDB parity trace: dump GAMU basis at representative nodes
      WRITE(0,'(A,Z8,A,Z8,A,Z8,A,Z8)')
     & 'F_GAM0 g0=',TRANSFER(GAMU(1,1),1),
     & ' g1=',TRANSFER(GAMU(2,1),1),
     & ' g80=',TRANSFER(GAMU(81,1),1),
     & ' gLast=',TRANSFER(GAMU(N+1,1),1)
      WRITE(0,'(A,Z8,A,Z8,A,Z8,A,Z8)')
     & 'F_GAM1 g0=',TRANSFER(GAMU(1,2),1),
     & ' g1=',TRANSFER(GAMU(2,2),1),
     & ' g80=',TRANSFER(GAMU(81,2),1),
     & ' gLast=',TRANSFER(GAMU(N+1,2),1)
C
      IF(LDEBUG50) THEN
       DO 45 I=1, N+1
         WRITE(50,9908) I, GAMU(I,1), GAMU(I,2)
   45  CONTINUE
      ENDIF
C
      DO 46 I=1, N+1
        CALL TRACE_BASIS_ENTRY('GGCALC', 'basis_gamma_alpha0',
     &                         I, GAMU(I,1))
        CALL TRACE_BASIS_ENTRY('GGCALC', 'basis_gamma_alpha90',
     &                         I, GAMU(I,2))
   46 CONTINUE
C
C---- set inviscid alpha=0,90 surface speeds for this geometry
      DO 50 I=1, N
        QINVU(I,1) = GAMU(I,1)
        QINVU(I,2) = GAMU(I,2)
   50 CONTINUE
C
      LGAMU = .TRUE.
C
      CALL TRACE_EXIT('GGCALC')
C
      RETURN
 9907 FORMAT('AIJ_ROW I=',I4,' J=',I4,' VAL=',1PE15.8)
 9908 FORMAT('GAMU_ROW I=',I4,' U1=',1PE15.8,' U2=',1PE15.8)
      END



      SUBROUTINE QWCALC
C---------------------------------------------------------------
C     Sets inviscid tangential velocity for alpha = 0, 90
C     on wake due to freestream and airfoil surface vorticity.
C---------------------------------------------------------------
      INCLUDE 'XFOIL.INC'
C
      CALL TRACE_ENTER('QWCALC')
C
C---- first wake point (same as TE)
      QINVU(N+1,1) = QINVU(N,1)
      QINVU(N+1,2) = QINVU(N,2)
C
C---- rest of wake
      DO 10 I=N+2, N+NW
        CALL PSILIN(I,X(I),Y(I),NX(I),NY(I),PSI,PSI_NI,.FALSE.,.FALSE.)
        QINVU(I,1) = QTAN1
        QINVU(I,2) = QTAN2
        IF(I.LE.N+12) THEN
         WRITE(0,'(A,I3,5(A,Z8))')
     &    'F_WSPD iw=',I-N-1,
     &    ' q1=',TRANSFER(QTAN1,1),
     &    ' q2=',TRANSFER(QTAN2,1),
     &    ' X=',TRANSFER(X(I),1),
     &    ' NX=',TRANSFER(NX(I),1),
     &    ' NY=',TRANSFER(NY(I),1)
        ENDIF
        IF(I.EQ.N+7) THEN
         WRITE(0,'(A,Z8,A,Z8,A,Z8,A,Z8,A,Z8,A,Z8)')
     &    'F_WGEOM7 X=',TRANSFER(X(I),1),
     &    ' Y=',TRANSFER(Y(I),1),
     &    ' NX=',TRANSFER(NX(I),1),
     &    ' NY=',TRANSFER(NY(I),1),
     &    ' q1=',TRANSFER(QTAN1,1),
     &    ' q2=',TRANSFER(QTAN2,1)
        ENDIF
   10 CONTINUE
C
      CALL TRACE_EXIT('QWCALC')
      RETURN
      END


      SUBROUTINE QDCALC
C-----------------------------------------------------
C     Calculates source panel influence coefficient
C     matrix for current airfoil and wake geometry.
C-----------------------------------------------------
      INCLUDE 'XFOIL.INC'
      LOGICAL LDEBUG50
      CHARACTER*64 BAKSUB_TRACE_CONTEXT
      CHARACTER*256 TRLINE
      COMMON /TRACE_BAKSUB_CTX/ BAKSUB_TRACE_CONTEXT
C
      CALL TRACE_ENTER('QDCALC')
      WRITE(*,*) 'Calculating source influence matrix ...'
      INQUIRE(UNIT=50, OPENED=LDEBUG50)
C
      IF(.NOT.LADIJ) THEN
C
       DO 5 I=1, N
        IF(LDEBUG50) WRITE(50,9906) I, X(I), Y(I), NX(I), NY(I),
     &                               APANEL(I)
        CALL TRACE_PANEL_NODE('QDCALC', I, X(I), Y(I),
     &                        XP(I), YP(I),
     &                        NX(I), NY(I), APANEL(I))
    5  CONTINUE
C
C----- calculate source influence matrix for airfoil surface if it doesn't exist
       DO 10 J=1, N
C
C------- trace the unsolved airfoil source column before the BAKSUB solve
         DO 101 I=1, N+1
           CALL TRACE_COLUMN_ENTRY('QDCALC', 'airfoil_rhs_column_entry',
     &                           J, I, BIJ(I,J))
  101    CONTINUE
C
C------- multiply each dPsi/Sig vector by inverse of factored dPsi/dGam matrix
         CALL BAKSUB(IQX,N+1,AIJ,AIJPIV,BIJ(1,J))
C
C------- trace the solved airfoil source column after the BAKSUB solve
         DO 102 I=1, N+1
           CALL TRACE_COLUMN_ENTRY('QDCALC', 'airfoil_sol_column_entry',
     &                           J, I, BIJ(I,J))
  102    CONTINUE
C
C------- store resulting dGam/dSig = dQtan/dSig vector
         DO 105 I=1, N
           DIJ(I,J) = BIJ(I,J)
  105    CONTINUE
C
   10  CONTINUE
       LADIJ = .TRUE.
C
C---- GDB parity: dump DIJ at specific elements
      WRITE(0,'(A,Z8,A,Z8,A,Z8,A,Z8)')
     & 'F_DIJ d11=',TRANSFER(DIJ(1,1),1),
     & ' d1_40=',TRANSFER(DIJ(1,40),1),
     & ' d40_1=',TRANSFER(DIJ(40,1),1),
     & ' d40_40=',TRANSFER(DIJ(40,40),1)
      WRITE(0,'(A,Z8,A,Z8,A,Z8,A,Z8)')
     & 'F_DIJ d1_80=',TRANSFER(DIJ(1,80),1),
     & ' d80_1=',TRANSFER(DIJ(80,1),1),
     & ' d80_80=',TRANSFER(DIJ(80,80),1),
     & ' d41_1=',TRANSFER(DIJ(41,1),1)
C---- GDB parity: dump wake DIJ columns
      IF(N+NW .GE. 82) THEN
       WRITE(0,'(A,Z8,A,Z8,A,Z8)')
     &  'F_DIJ_W d41_82=',TRANSFER(DIJ(41,82),1),
     &  ' d41_85=',TRANSFER(DIJ(41,85),1),
     &  ' d41_92=',TRANSFER(DIJ(41,92),1)
       WRITE(0,'(A,Z8,A,Z8,A,Z8)')
     &  'F_DIJ_W d1_82=',TRANSFER(DIJ(1,82),1),
     &  ' d1_85=',TRANSFER(DIJ(1,85),1),
     &  ' d40_82=',TRANSFER(DIJ(40,82),1)
      ENDIF
C
      ENDIF
C
C---- set up coefficient matrix of dPsi/dm on airfoil surface
      DO 20 I=1, N
        CALL PSWLIN(I,X(I),Y(I),NX(I),NY(I),PSI,PSI_N)
        DO 202 J=N+1, N+NW
          BIJ(I,J) = -DZDM(J)
  202   CONTINUE
        IF(LDEBUG50 .AND. I.EQ.IPAN(2,1)) THEN
          DO 203 J=N+1, MIN(N+NW,N+5)
            WRITE(50,9902) I, J, BIJ(I,J)
            WRITE(TRLINE,9902) I, J, BIJ(I,J)
            CALL TRACE_TEXT('QDCALC', 'wake_rhs', TRLINE)
  203     CONTINUE
        ENDIF
   20 CONTINUE
C
C---- set up Kutta condition (no direct source influence)
      DO 32 J=N+1, N+NW
        BIJ(N+1,J) = 0.
   32 CONTINUE
C
C---- sharp TE gamma extrapolation also has no source influence
      IF(SHARP) THEN
       DO 34 J=N+1, N+NW
         BIJ(N,J) = 0.
   34  CONTINUE
      ENDIF
C
      IF(LDEBUG50 .AND. NW.GE.1) THEN
       DO 35 I=1, N+1
         WRITE(50,9904) I, BIJ(I,N+1)
         WRITE(TRLINE,9904) I, BIJ(I,N+1)
         CALL TRACE_TEXT('QDCALC', 'wake_rhs_column', TRLINE)
         DO 351 J=N+1, N+NW
           CALL TRACE_COLUMN_ENTRY('QDCALC', 'wake_rhs_column_entry',
     &                             J-N, I, BIJ(I,J))
  351    CONTINUE
   35  CONTINUE
      ENDIF
C
C---- trace BIJ before BAKSUB at row 77
      DO 39 J=N+1, N+NW
        WRITE(0,'(A,I3,A,Z8,A,Z8,A,Z8)')
     &   'F_WBIJ jw=',J-N,
     &   ' rhs77=',TRANSFER(BIJ(77,J),1),
     &   ' rhs1=',TRANSFER(BIJ(1,J),1),
     &   ' rhs40=',TRANSFER(BIJ(40,J),1)
   39 CONTINUE
C---- multiply by inverse of factored dPsi/dGam matrix
      DO 40 J=N+1, N+NW
        IF(J.EQ.N+1) THEN
          BAKSUB_TRACE_CONTEXT = 'qdcalc_wake_column_1_single'
        ELSE
          BAKSUB_TRACE_CONTEXT = ' '
        ENDIF
        CALL BAKSUB(IQX,N+1,AIJ,AIJPIV,BIJ(1,J))
        BAKSUB_TRACE_CONTEXT = ' '
        IF(LDEBUG50 .AND. J.LE.N+5) THEN
          WRITE(50,9903) IPAN(2,1), J, BIJ(IPAN(2,1),J)
          WRITE(TRLINE,9903) IPAN(2,1), J, BIJ(IPAN(2,1),J)
          CALL TRACE_TEXT('QDCALC', 'wake_sol', TRLINE)
        ENDIF
   40 CONTINUE
C
      IF(LDEBUG50 .AND. NW.GE.1) THEN
       DO 45 I=1, N+1
         WRITE(50,9905) I, BIJ(I,N+1)
         WRITE(TRLINE,9905) I, BIJ(I,N+1)
         CALL TRACE_TEXT('QDCALC', 'wake_sol_column', TRLINE)
         DO 451 J=N+1, N+NW
           CALL TRACE_COLUMN_ENTRY('QDCALC', 'wake_sol_column_entry',
     &                             J-N, I, BIJ(I,J))
  451    CONTINUE
   45  CONTINUE
      ENDIF
C
C---- set the source influence matrix for the wake sources
      DO 50 I=1, N
        DO 510 J=N+1, N+NW
          DIJ(I,J) = BIJ(I,J)
  510   CONTINUE
   50 CONTINUE
C---- trace wake DIJ at row 77 for each wake column
      DO 51 J=N+1, N+NW
        WRITE(0,'(A,I3,A,I4,A,Z8)')
     &   'F_WDIJ jw=',J-N,' col=',J,
     &   ' dij77=',TRANSFER(DIJ(77,J),1)
   51 CONTINUE
C
C**** Now we need to calculate the influence of sources on the wake velocities
C
C---- calculcate dQtan/dGam and dQtan/dSig at the wake points
      DO 70 I=N+1, N+NW
C
        IW = I-N
C
C------ airfoil contribution at wake panel node
        CALL PSILIN(I,X(I),Y(I),NX(I),NY(I),PSI,PSI_N,.FALSE.,.TRUE.)
C
        DO 710 J=1, N
          CIJ(IW,J) = DQDG(J)
  710   CONTINUE
C  
        DO 720 J=1, N
          DIJ(I,J) = DQDM(J)
  720   CONTINUE
        IF(IW.EQ.4 .OR. IW.EQ.5) THEN
         WRITE(*,'(A,I2,A,Z8,A,Z8)')
     &    'F_WROW IW=',IW,' DQDM78=',TRANSFER(DQDM(78),1),
     &    ' CIJ78=',TRANSFER(CIJ(IW,78),1)
        ENDIF
C
C------ wake contribution
        CALL PSWLIN(I,X(I),Y(I),NX(I),NY(I),PSI,PSI_N)
C
        DO 730 J=N+1, N+NW
          DIJ(I,J) = DQDM(J)
  730   CONTINUE
C
   70 CONTINUE
C
C---- add on effect of all sources on airfoil vorticity which effects wake Qtan
      DO 80 I=N+1, N+NW
        IW = I-N
C
C------ airfoil surface source contribution first
        DO 810 J=1, N
          SUM = 0.
          DO 8100 K=1, N
            SUM = SUM + CIJ(IW,K)*DIJ(K,J)
 8100     CONTINUE
          DIJ(I,J) = DIJ(I,J) + SUM
  810   CONTINUE
C
C------ wake source contribution next
        DO 820 J=N+1, N+NW
          SUM = 0.
          DO 8200 K=1, N
            SUM = SUM + CIJ(IW,K)*BIJ(K,J)
 8200     CONTINUE
          IF(IW.EQ.5 .AND. J.EQ.I) THEN
           WRITE(*,'(A,Z8,A,Z8,A,Z8)')
     &      'F_WKDIAG_PARTS pre=',TRANSFER(DIJ(I,J),1),
     &      ' sum=',TRANSFER(SUM,1),
     &      ' bij=',TRANSFER(BIJ(1,J),1)
          ENDIF
          DIJ(I,J) = DIJ(I,J) + SUM
  820   CONTINUE
C
   80 CONTINUE
C
C---- make sure first wake point has same velocity as trailing edge
      DO 90 J=1, N+NW
        DIJ(N+1,J) = DIJ(N,J)
   90 CONTINUE
C
      LWDIJ = .TRUE.
C
C---- Dump wake row 5 DIJ at c78 + wake diagonal (after indirect sum)
      WRITE(*,'(A,Z8,A,Z8,A,I4)')
     & 'F_WROW5_FINAL c78=',TRANSFER(DIJ(N+5,78),1),
     & ' wkDiag=',TRANSFER(DIJ(N+5,N+5),1),
     & ' wkD_col=',N+5
C---- GDB parity: dump COMPLETE wake DIJ (after all fills)
      IF(N+NW .GE. 92) THEN
       WRITE(0,'(A,Z8,A,Z8,A,Z8)')
     &  'F_DIJ_W2 d41_82=',TRANSFER(DIJ(41,82),1),
     &  ' d41_85=',TRANSFER(DIJ(41,85),1),
     &  ' d41_92=',TRANSFER(DIJ(41,92),1)
       WRITE(0,'(A,Z8,A,Z8,A,Z8)')
     &  'F_DIJ_W2 d1_82=',TRANSFER(DIJ(1,82),1),
     &  ' d1_85=',TRANSFER(DIJ(1,85),1),
     &  ' d40_82=',TRANSFER(DIJ(40,82),1)
      ENDIF
C
      RETURN
 9902 FORMAT('WAKE_RHS I=',I4,' J=',I4,' BIJ=',1PE15.8)
 9903 FORMAT('WAKE_SOL I=',I4,' J=',I4,' BIJ=',1PE15.8)
 9904 FORMAT('WAKE_RHS_COL I=',I4,' BIJ=',1PE15.8)
 9905 FORMAT('WAKE_SOL_COL I=',I4,' BIJ=',1PE15.8)
 9906 FORMAT('PANEL_NODE I=',I4,' X=',1PE15.8,' Y=',1PE15.8,
     &       ' NX=',1PE15.8,' NY=',1PE15.8,' APAN=',1PE15.8)
      END


      SUBROUTINE XYWAKE
C-----------------------------------------------------
C     Sets wake coordinate array for current surface 
C     vorticity and/or mass source distributions.
C-----------------------------------------------------
      INCLUDE 'XFOIL.INC'
C
      WRITE(*,*) 'Calculating wake trajectory ...'
C
C---- number of wake points
      NW = N/8 + 2
      IF(NW.GT.IWX) THEN
       WRITE(*,*)
     &  'Array size (IWX) too small.  Last wake point index reduced.'
       NW = IWX
      ENDIF
C
      UPPERDELTA = S(2) - S(1)
      LOWERDELTA = S(N) - S(N-1)
      DS1 = 0.5*(UPPERDELTA + LOWERDELTA)
      CALL TRACE_WAKE_SPACING_INPUT('SETBL', S(1), S(2), S(N-1), S(N),
     &                              UPPERDELTA, LOWERDELTA, DS1)
      CALL SETEXP(SNEW(N+1),DS1,WAKLEN*CHORD,NW)
      DO 5 J=N+1, N+NW
        IF(J.EQ.N+1) THEN
          DSWAKE = 0.0
        ELSE
          DSWAKE = SNEW(J) - SNEW(J-1)
        ENDIF
        CALL TRACE_WAKE_SPACING('SETBL', J-N, SNEW(J), DSWAKE, DS1)
    5 CONTINUE
C
      XTE = 0.5*(X(1)+X(N))
      YTE = 0.5*(Y(1)+Y(N))
C
C---- set first wake point a tiny distance behind TE
      I = N+1
      SX = 0.5*(YP(N) - YP(1))
      SY = 0.5*(XP(1) - XP(N))
      SMOD = SQRT(SX**2 + SY**2)
      NX(I) = SX / SMOD
      NY(I) = SY / SMOD
      X(I) = XTE - 0.0001*NY(I)
      Y(I) = YTE + 0.0001*NX(I)
      S(I) = S(N)
      WRITE(0,'(A,Z8,A,Z8,A,Z8,A,Z8)')
     & 'F_WK1 X=',TRANSFER(X(I),1),
     & ' Y=',TRANSFER(Y(I),1),
     & ' NX=',TRANSFER(NX(I),1),
     & ' NY=',TRANSFER(NY(I),1)
C
C---- calculate streamfunction gradient components at first point
      CALL PSILIN(I,X(I),Y(I),1.0,0.0,PSI,PSI_X,.FALSE.,.FALSE.)
      CALL PSILIN(I,X(I),Y(I),0.0,1.0,PSI,PSI_Y,.FALSE.,.FALSE.)
C
C---- set unit vector normal to wake at first point
      NX(I+1) = -PSI_X / SQRT(PSI_X**2 + PSI_Y**2)
      NY(I+1) = -PSI_Y / SQRT(PSI_X**2 + PSI_Y**2)
      WRITE(0,'(A,Z8,A,Z8,A,Z8,A,Z8)')
     & 'F_WK1_PSI psiX=',TRANSFER(PSI_X,1),
     & ' psiY=',TRANSFER(PSI_Y,1),
     & ' nx2=',TRANSFER(NX(I+1),1),
     & ' ny2=',TRANSFER(NY(I+1),1)
C
C---- set angle of wake panel normal
      APANEL(I) = ATAN2( PSI_Y , PSI_X )
      CALL TRACE_WAKE_PANEL_STATE('QDCALC', 1, I, X(I), Y(I),
     &                            PSI_X, PSI_Y,
     &                            SQRT(PSI_X**2 + PSI_Y**2),
     &                            APANEL(I), NX(I), NY(I),
     &                            NX(I+1), NY(I+1))
C
C---- set rest of wake points
      DO 10 I=N+2, N+NW
        DS = SNEW(I) - SNEW(I-1)
C
C------ set new point DS downstream of last point
        X(I) = X(I-1) - DS*NY(I)
        Y(I) = Y(I-1) + DS*NX(I)
        S(I) = S(I-1) + DS
C---- NACA 1410 Re=500K wake march debug: step-by-step intermediate
        IF(I-N.LE.6) THEN
          WRITE(0,'(A,I3,7(A,Z8))')
     &     'F_WKM i=',I-N,
     &     ' ds=',TRANSFER(DS,1),
     &     ' nx=',TRANSFER(NX(I),1),
     &     ' ny=',TRANSFER(NY(I),1),
     &     ' xp=',TRANSFER(X(I-1),1),
     &     ' yp=',TRANSFER(Y(I-1),1),
     &     ' xn=',TRANSFER(X(I),1),
     &     ' yn=',TRANSFER(Y(I),1)
        ENDIF
        CALL TRACE_WAKE_STEP_TERMS('SETBL', I-N+1, DS,
     &                             X(I-1), Y(I-1), NX(I), NY(I),
     &                             X(I), Y(I))
C
        IF(I.EQ.N+NW) GO TO 10
C
C------- calculate normal vector for next point
         CALL PSILIN(I,X(I),Y(I),1.0,0.0,PSI,PSI_X,.FALSE.,.FALSE.)
         CALL PSILIN(I,X(I),Y(I),0.0,1.0,PSI,PSI_Y,.FALSE.,.FALSE.)
C
         NX(I+1) = -PSI_X / SQRT(PSI_X**2 + PSI_Y**2)
         NY(I+1) = -PSI_Y / SQRT(PSI_X**2 + PSI_Y**2)
         WRITE(0,'(A,I3,A,Z8,A,Z8,A,Z8,A,Z8,A,Z8,A,Z8)')
     &    'F_WKMARCH i=',I-N,' X=',TRANSFER(X(I),1),
     &    ' Y=',TRANSFER(Y(I),1),
     &    ' psiX=',TRANSFER(PSI_X,1),
     &    ' psiY=',TRANSFER(PSI_Y,1),
     &    ' nx_n=',TRANSFER(NX(I+1),1),
     &    ' ny_n=',TRANSFER(NY(I+1),1)
C
C------- set angle of wake panel normal
         APANEL(I) = ATAN2( PSI_Y , PSI_X )
         CALL TRACE_WAKE_PANEL_STATE('QDCALC', I-N, I, X(I), Y(I),
     &                               PSI_X, PSI_Y,
     &                               SQRT(PSI_X**2 + PSI_Y**2),
     &                               APANEL(I), NX(I), NY(I),
     &                               NX(I+1), NY(I+1))
C
   10 CONTINUE
C
C---- trace exact wake geometry words so focused wake-source diagnostics
C     can replay XYWAKE inputs without dump-text rounding loss.
      DO 15 IW=1, NW
        I = N + IW
        CALL TRACE_BASIS_ENTRY('QDCALC', 'wake_geometry_x', IW, X(I))
        CALL TRACE_BASIS_ENTRY('QDCALC', 'wake_geometry_y', IW, Y(I))
        CALL TRACE_BASIS_ENTRY('QDCALC', 'wake_geometry_nx', IW, NX(I))
        CALL TRACE_BASIS_ENTRY('QDCALC', 'wake_geometry_ny', IW, NY(I))
        IF(I .LT. N+NW) THEN
          CALL TRACE_BASIS_ENTRY('QDCALC', 'wake_geometry_panel_angle',
     &                           IW, APANEL(I))
        ENDIF
   15 CONTINUE
C
C---- set wake presence flag and corresponding alpha
      LWAKE = .TRUE.
      AWAKE =  ALFA
C
C---- old source influence matrix is invalid for the new wake geometry
      LWDIJ = .FALSE.
C
      CALL TRACE_EXIT('QDCALC')
      RETURN
      END



      SUBROUTINE STFIND
C-----------------------------------------
C     Locates stagnation point arc length 
C     location SST and panel index IST.
C-----------------------------------------
      INCLUDE 'XFOIL.INC'
C
      INTEGER JTRACE
C
      CALL TRACE_ENTER('STFIND')
C
      WRITE(0,'(A)') 'F_STFIND_ENTRY'
      DO 5 I=90, MIN(N,105)
        WRITE(0,'(A,I3,A,Z8)') 'F_GAM_STFIND i=',I,
     &   ' GAM=',TRANSFER(GAM(I),1)
    5 CONTINUE
      DO 10 I=1, N-1
        IF(GAM(I).GE.0.0 .AND. GAM(I+1).LT.0.0) THEN
         CALL TRACE_STAGNATION_CANDIDATE('STFIND', I,
     &        GAM(I), GAM(I+1), S(I), S(I+1),
     &        ABS(GAM(I)) + ABS(GAM(I+1)))
         GO TO 11
        ENDIF
   10 CONTINUE
C
      WRITE(*,*) 'STFIND: Stagnation point not found. Continuing ...'
      I = N/2
C
   11 CONTINUE
C
      JTRACE = MAX(1, I-2)
      IF(JTRACE+5 .LE. N) THEN
       CALL TRACE_STAGNATION_SPEED_WINDOW('STFIND', I, JTRACE,
     &      GAM(JTRACE), GAM(JTRACE+1), GAM(JTRACE+2),
     &      GAM(JTRACE+3), GAM(JTRACE+4), GAM(JTRACE+5))
      ENDIF
C
      IST = I
      DGAM = GAM(I+1) - GAM(I)
      DS = S(I+1) - S(I)
C
C---- trace the actual interpolation inputs so parity work can separate
C     a GAM/QINV mismatch from an SST arithmetic mismatch.
      IF(GAM(I) .LT. -GAM(I+1)) THEN
       CALL TRACE_STAGNATION_INTERPOLATION('STFIND', I,
     &      GAM(I), GAM(I+1), DGAM, DS, S(I), S(I+1), 1)
      ELSE
       CALL TRACE_STAGNATION_INTERPOLATION('STFIND', I,
     &      GAM(I), GAM(I+1), DGAM, DS, S(I), S(I+1), 0)
      ENDIF
C
C---- evaluate so as to minimize roundoff for very small GAM(I) or GAM(I+1)
      IF(GAM(I) .LT. -GAM(I+1)) THEN
       SST = S(I)   - DS*(GAM(I)  /DGAM)
      ELSE
       SST = S(I+1) - DS*(GAM(I+1)/DGAM)
      ENDIF
C---- trace STFIND inputs and result for parity debugging
      WRITE(0,'(A,I4,A,Z8,A,Z8,A,Z8,A,Z8,A,Z8)')
     &  'F_STFIND I=',I,
     &  ' GI=',TRANSFER(GAM(I),1),
     &  ' GIP=',TRANSFER(GAM(I+1),1),
     &  ' SI=',TRANSFER(S(I),1),
     &  ' SIP=',TRANSFER(S(I+1),1),
     &  ' SST=',TRANSFER(SST,1)
C
C---- tweak stagnation point if it falls right on a node (very unlikely)
      IF(SST .LE. S(I)  ) SST = S(I)   + 1.0E-7
      IF(SST .GE. S(I+1)) SST = S(I+1) - 1.0E-7
C
      SST_GO = (SST  - S(I+1))/DGAM
      SST_GP = (S(I) - SST   )/DGAM
C
      CALL TRACE_EXIT('STFIND')
      RETURN
      END


      SUBROUTINE IBLPAN
C-------------------------------------------------------------
C     Sets  BL location -> panel location  pointer array IPAN
C-------------------------------------------------------------
      INCLUDE 'XFOIL.INC'
C
      CALL TRACE_ENTER('IBLPAN')
C
C---- top surface first
      IS = 1
C
      IBL = 1
      DO 10 I=IST, 1, -1
        IBL = IBL+1
        IPAN(IBL,IS) = I
        VTI(IBL,IS) = 1.0
   10 CONTINUE
C
      IBLTE(IS) = IBL
      NBL(IS) = IBL
C
C---- bottom surface next
      IS = 2
C
      IBL = 1
      DO 20 I=IST+1, N
        IBL = IBL+1
        IPAN(IBL,IS) = I
        VTI(IBL,IS) = -1.0
   20 CONTINUE
C
C---- wake
      IBLTE(IS) = IBL
C
      DO 25 IW=1, NW
        I = N+IW
        IBL = IBLTE(IS)+IW
        IPAN(IBL,IS) = I
         VTI(IBL,IS) = -1.0
   25 CONTINUE
C
      NBL(IS) = IBLTE(IS) + NW
C
C---- upper wake pointers (for plotting only)
      DO 35 IW=1, NW
        IPAN(IBLTE(1)+IW,1) = IPAN(IBLTE(2)+IW,2)
         VTI(IBLTE(1)+IW,1) = 1.0
   35 CONTINUE
C
C
      IBLMAX = MAX(IBLTE(1),IBLTE(2)) + NW
      IF(IBLMAX.GT.IVX) THEN
        WRITE(*,*) ' ***  BL array overflow.'
        WRITE(*,*) ' ***  Increase IVX to at least', IBLMAX
        STOP
      ENDIF
C
      LIPAN = .TRUE.
      CALL TRACE_EXIT('IBLPAN')
      RETURN
      END


      SUBROUTINE XICALC
C-------------------------------------------------------------
C     Sets BL arc length array on each airfoil side and wake
C-------------------------------------------------------------
      INCLUDE 'XFOIL.INC'
      DATA XFEPS / 1.0E-7 /
C
      CALL TRACE_ENTER('XICALC')
C
C---- minimum xi node arc length near stagnation point
      XEPS = XFEPS*(S(N)-S(1))
C
      IS = 1
C
      XSSI(1,IS) = 0.
C
      DO 10 IBL=2, IBLTE(IS)
        I = IPAN(IBL,IS)
        XSSI(IBL,IS) = MAX( SST - S(I) , XEPS )
   10 CONTINUE
C
C
      IS = 2
C
      XSSI(1,IS) = 0.
C
      DO 20 IBL=2, IBLTE(IS)
        I = IPAN(IBL,IS)
        XSSI(IBL,IS) = MAX( S(I) - SST , XEPS )
   20 CONTINUE
C
C
      IS1 = 1
      IS2 = 2
C
      IBL1 = IBLTE(IS1) + 1
      XSSI(IBL1,IS1) = XSSI(IBL1-1,IS1)
C
      IBL2 = IBLTE(IS2) + 1
      XSSI(IBL2,IS2) = XSSI(IBL2-1,IS2)
C
      DO 25 IBL=IBLTE(IS)+2, NBL(IS)
        I = IPAN(IBL,IS)
        DXSSI = SQRT((X(I)-X(I-1))**2 + (Y(I)-Y(I-1))**2)
C
        IBL1 = IBLTE(IS1) + IBL - IBLTE(IS)
        IBL2 = IBLTE(IS2) + IBL - IBLTE(IS)
        XSSI(IBL1,IS1) = XSSI(IBL1-1,IS1) + DXSSI
        XSSI(IBL2,IS2) = XSSI(IBL2-1,IS2) + DXSSI
   25 CONTINUE
C
C---- trailing edge flap length to TE gap ratio
      TELRAT = 2.50
C
C---- set up parameters for TE flap cubics
C
ccc   DWDXTE = YP(1)/XP(1) + YP(N)/XP(N)    !!! BUG  2/2/95
C
      CROSP = (XP(1)*YP(N) - YP(1)*XP(N))
     &      / SQRT(  (XP(1)**2 + YP(1)**2)
     &              *(XP(N)**2 + YP(N)**2) )
      DWDXTE = CROSP / SQRT(1.0 - CROSP**2)
C
C---- limit cubic to avoid absurd TE gap widths
      DWDXTE = MAX(DWDXTE,-3.0/TELRAT)
      DWDXTE = MIN(DWDXTE, 3.0/TELRAT)
C
      AA =  3.0 + TELRAT*DWDXTE
      BB = -2.0 - TELRAT*DWDXTE
C
      IF(SHARP) THEN
       DO 30 IW=1, NW
         WGAP(IW) = 0.
   30  CONTINUE
      ELSE
C----- set TE flap (wake gap) array
       IS = 2
       DO 35 IW=1, NW
         IBL = IBLTE(IS) + IW
         ZN = 1.0 - (XSSI(IBL,IS)-XSSI(IBLTE(IS),IS)) / (TELRAT*ANTE)
         WGAP(IW) = 0.
         IF(ZN.GE.0.0) WGAP(IW) = ANTE * (AA + BB*ZN)*ZN**2
         IF(IW.EQ.1) THEN
          WRITE(0,'(A,Z8,A,Z8,A,Z8,A,Z8,A,Z8,A,Z8)')
     &     'F_WGAP_D AA=',TRANSFER(AA,1),
     &     ' BB=',TRANSFER(BB,1),
     &     ' DWDXTE=',TRANSFER(DWDXTE,1),
     &     ' ANTE=',TRANSFER(ANTE,1),
     &     ' AA+BB=',TRANSFER(AA+BB,1),
     &     ' AABBZN=',TRANSFER((AA+BB*ZN)*ZN**2,1)
         ENDIF
         WRITE(0,'(A,I3,A,Z8,A,Z8,A,Z8,A,Z8)')
     &    'F_WGAP IW=',IW,
     &    ' XSSI=',TRANSFER(XSSI(IBL,IS),1),
     &    ' XSSI_TE=',TRANSFER(XSSI(IBLTE(IS),IS),1),
     &    ' ZN=',TRANSFER(ZN,1),
     &    ' WGAP=',TRANSFER(WGAP(IW),1)
   35  CONTINUE
      ENDIF
C
      CALL TRACE_EXIT('XICALC')
      RETURN
      END


      SUBROUTINE UICALC
C--------------------------------------------------------------
C     Sets inviscid Ue from panel inviscid tangential velocity
C--------------------------------------------------------------
      INCLUDE 'XFOIL.INC'
C
      CALL TRACE_ENTER('UICALC')
C
      DO 10 IS=1, 2
        UINV  (1,IS) = 0.
        UINV_A(1,IS) = 0.
        DO 110 IBL=2, NBL(IS)
          I = IPAN(IBL,IS)
          UINV  (IBL,IS) = VTI(IBL,IS)*QINV  (I)
          UINV_A(IBL,IS) = VTI(IBL,IS)*QINV_A(I)
          IF (IBL.LE.8) THEN
            WRITE(0,'(A,I1,A,I2,A,I3,3(A,Z8))')
     &        'F_UEINV s=',IS,' ibl=',IBL,' ipan=',I,
     &        ' VTI=',TRANSFER(VTI(IBL,IS),1),
     &        ' QINV=',TRANSFER(QINV(I),1),
     &        ' UEINV=',TRANSFER(UINV(IBL,IS),1)
          ENDIF
  110   CONTINUE
   10 CONTINUE
C
      CALL TRACE_EXIT('UICALC')
      RETURN
      END


      SUBROUTINE UECALC
C--------------------------------------------------------------
C     Sets viscous Ue from panel viscous tangential velocity
C--------------------------------------------------------------
      INCLUDE 'XFOIL.INC'
C
      DO 10 IS=1, 2
        UEDG(1,IS) = 0.
        DO 110 IBL=2, NBL(IS)
          I = IPAN(IBL,IS)
          UEDG(IBL,IS) = VTI(IBL,IS)*QVIS(I)
  110   CONTINUE
   10 CONTINUE
C
      RETURN
      END


      SUBROUTINE QVFUE
C--------------------------------------------------------------
C     Sets panel viscous tangential velocity from viscous Ue
C--------------------------------------------------------------
      INCLUDE 'XFOIL.INC'
C
      DO 1 IS=1, 2
        DO 10 IBL=2, NBL(IS)
          I = IPAN(IBL,IS)
          QVIS(I) = VTI(IBL,IS)*UEDG(IBL,IS)
   10   CONTINUE
    1 CONTINUE
C
      RETURN
      END


      SUBROUTINE QISET
C-------------------------------------------------------
C     Sets inviscid panel tangential velocity for
C     current alpha.
C-------------------------------------------------------
      INCLUDE 'XFOIL.INC'
C
      CALL TRACE_ENTER('QISET')
C
      COSA = COS(ALFA)
      SINA = SIN(ALFA)
C
      DO 5 I=1, N+NW
        QINV  (I) =  COSA*QINVU(I,1) + SINA*QINVU(I,2)
        QINV_A(I) = -SINA*QINVU(I,1) + COSA*QINVU(I,2)
    5 CONTINUE
C
      CALL TRACE_EXIT('QISET')
      RETURN
      END


      SUBROUTINE GAMQV
      INCLUDE 'XFOIL.INC'
C
      DO 10 I=1, N
        GAM(I)   = QVIS(I)
        GAM_A(I) = QINV_A(I)
   10 CONTINUE
C
      RETURN
      END


      SUBROUTINE STMOVE
C---------------------------------------------------
C     Moves stagnation point location to new panel.
C---------------------------------------------------
      INCLUDE 'XFOIL.INC'
C
      CALL TRACE_ENTER('STMOVE')
C
      SSTOLD = SST
C---- locate new stagnation point arc length SST from GAM distribution
      ISTOLD = IST
      CALL STFIND
C
      WRITE(0,'(A,A,Z8,A,Z8,A,I4,A,I4)') 'F_SSTVAL',
     &  ' sstOld=',TRANSFER(SSTOLD,1),
     &  ' sstNew=',TRANSFER(SST,1),
     &  ' istOld=',ISTOLD,' istNew=',IST
C
      IF(ISTOLD.EQ.IST) THEN
C
C----- recalculate new arc length array
       CALL XICALC
C
      ELSE
C
CCC       WRITE(*,*) 'STMOVE: Resetting stagnation point'
C
C----- set new BL position -> panel position  pointers
       CALL IBLPAN
C
C----- set new inviscid BL edge velocity UINV from QINV
       CALL UICALC
C
C----- recalculate new arc length array
       CALL XICALC
C
C----- set  BL position -> system line  pointers
       CALL IBLSYS
C
       IF(IST.GT.ISTOLD) THEN
C------ increase in number of points on top side (IS=1)
        IDIF = IST-ISTOLD
C
        ITRAN(1) = ITRAN(1) + IDIF
        ITRAN(2) = ITRAN(2) - IDIF
C
C------ move top side BL variables downstream
        DO 110 IBL=NBL(1), IDIF+2, -1
          CTAU(IBL,1) = CTAU(IBL-IDIF,1)
          THET(IBL,1) = THET(IBL-IDIF,1)
          DSTR(IBL,1) = DSTR(IBL-IDIF,1)
          UEDG(IBL,1) = UEDG(IBL-IDIF,1)
  110   CONTINUE            
C
C------ set BL variables between old and new stagnation point
        DUDX = UEDG(IDIF+2,1)/XSSI(IDIF+2,1)
        DO 115 IBL=IDIF+1, 2, -1
          CTAU(IBL,1) = CTAU(IDIF+2,1)
          THET(IBL,1) = THET(IDIF+2,1)
          DSTR(IBL,1) = DSTR(IDIF+2,1)
          UEDG(IBL,1) = DUDX * XSSI(IBL,1)
  115   CONTINUE
C
C------ move bottom side BL variables upstream
        DO 120 IBL=2, NBL(2)
          CTAU(IBL,2) = CTAU(IBL+IDIF,2)
          THET(IBL,2) = THET(IBL+IDIF,2)
          DSTR(IBL,2) = DSTR(IBL+IDIF,2)
          UEDG(IBL,2) = UEDG(IBL+IDIF,2)
  120   CONTINUE            
C
       ELSE
C------ increase in number of points on bottom side (IS=2)
        IDIF = ISTOLD-IST
C
        ITRAN(1) = ITRAN(1) - IDIF
        ITRAN(2) = ITRAN(2) + IDIF
C
C------ move bottom side BL variables downstream
        DO 210 IBL=NBL(2), IDIF+2, -1
          CTAU(IBL,2) = CTAU(IBL-IDIF,2)
          THET(IBL,2) = THET(IBL-IDIF,2)
          DSTR(IBL,2) = DSTR(IBL-IDIF,2)
          UEDG(IBL,2) = UEDG(IBL-IDIF,2)
  210   CONTINUE            
C
C------ set BL variables between old and new stagnation point
        DUDX = UEDG(IDIF+2,2)/XSSI(IDIF+2,2)


c        write(*,*) 'idif Ue xi dudx', 
c     &    idif, UEDG(idif+2,2), xssi(idif+2,2), dudx

        DO 215 IBL=IDIF+1, 2, -1
          CTAU(IBL,2) = CTAU(IDIF+2,2)
          THET(IBL,2) = THET(IDIF+2,2)
          DSTR(IBL,2) = DSTR(IDIF+2,2)
          UEDG(IBL,2) = DUDX * XSSI(IBL,2)
  215   CONTINUE

c        write(*,*) 'Uenew xinew', idif+1, uedg(idif+1,2), xssi(idif+1,2)

C
C------ move top side BL variables upstream
        DO 220 IBL=2, NBL(1)
          IF(IBL.EQ.27) THEN
           WRITE(0,'(A,I2,A,Z8,A,I4,A,Z8)')
     &      'F_SHIFT27 it=',ITER,
     &      ' src_T=',TRANSFER(THET(IBL+IDIF,1),1),
     &      ' src=',IBL+IDIF,
     &      ' old_T=',TRANSFER(THET(IBL,1),1)
          ENDIF
          CTAU(IBL,1) = CTAU(IBL+IDIF,1)
          THET(IBL,1) = THET(IBL+IDIF,1)
          DSTR(IBL,1) = DSTR(IBL+IDIF,1)
          UEDG(IBL,1) = UEDG(IBL+IDIF,1)
  220   CONTINUE            
       ENDIF
C
C----- tweak Ue so it's not zero, in case stag. point is right on node
       UEPS = 1.0E-7
       DO IS = 1, 2
         DO IBL = 2, NBL(IS)
           I = IPAN(IBL,IS)
           IF(UEDG(IBL,IS).LE.UEPS) THEN
            UEDG(IBL,IS) = UEPS
            QVIS(I) = VTI(IBL,IS)*UEPS
            GAM(I)  = VTI(IBL,IS)*UEPS
           ENDIF
         ENDDO
       ENDDO
C
      ENDIF
C
C---- set new mass array since Ue has been tweaked
      DO 50 IS=1, 2
        DO 510 IBL=2, NBL(IS)
          MASS(IBL,IS) = DSTR(IBL,IS)*UEDG(IBL,IS)
  510   CONTINUE
   50 CONTINUE
C
      CALL TRACE_EXIT('STMOVE')
      RETURN
      END


      SUBROUTINE UESET
C---------------------------------------------------------
C     Sets Ue from inviscid Ue plus all source influence
C---------------------------------------------------------
      INCLUDE 'XFOIL.INC'
      CHARACTER*256 TRLINE
      CHARACTER*8 UINVBITS, DUIABITS, DUIWBITS, USAVBITS
C
      CALL TRACE_ENTER('UESET')
C
      DO 1 IS=1, 2
        DO 10 IBL=2, NBL(IS)
          I = IPAN(IBL,IS)
C
          DUI = 0.
          DUIA = 0.
          DUIW = 0.
          DO 100 JS=1, 2
            DO 1000 JBL=2, NBL(JS)
              J  = IPAN(JBL,JS)
              UE_M = -VTI(IBL,IS)*VTI(JBL,JS)*DIJ(I,J)
              DUI = DUI + UE_M*MASS(JBL,JS)
              IF(IS.EQ.2 .AND. IBL.EQ.29 .AND. JS.EQ.2
     &           .AND. (JBL.EQ.104 .OR. JBL.EQ.105)) THEN
               WRITE(0,'(A,I3,A,Z8,A,Z8,A,Z8)')
     &          'F_DUI29D JBL=',JBL,
     &          ' DUI=',TRANSFER(DUI,1),
     &          ' M=',TRANSFER(MASS(JBL,JS),1),
     &          ' C=',TRANSFER(UE_M*MASS(JBL,JS),1)
              ENDIF
              IF(IS.EQ.1 .AND. IBL.EQ.53 .AND.
     &           JS.EQ.2 .AND. JBL.GE.81 .AND. JBL.LE.87) THEN
               WRITE(0,782) JS,JBL,
     &          TRANSFER(DUI,1),TRANSFER(MASS(JBL,JS),1),
     &          TRANSFER(UE_M,1)
 782           FORMAT('F_DUI53 JS=',I1,' JBL=',I3,
     &          3(1X,Z8.8))
              ENDIF
              IF(IS.EQ.1 .AND. IBL.EQ.2) THEN
               WRITE(0,'(A,I2,A,I3,A,I4,A,I4,A,Z8,A,Z8,A,Z8)')
     &          'F_DUI2 JS=',JS,' JBL=',JBL,
     &          ' I=',I,' J=',J,
     &          ' MASS=',TRANSFER(MASS(JBL,JS),1),
     &          ' UEM=',TRANSFER(UE_M,1),
     &          ' DUI=',TRANSFER(DUI,1)
              ENDIF
              IF(IS.EQ.2 .AND. IBL.EQ.88) THEN
               WRITE(*,'(A,I2,A,I3,A,Z8)')
     &          'F_DUI88 JS=',JS,' JBL=',JBL,
     &          ' DUI=',TRANSFER(DUI,1)
              ENDIF
              IF(IS.EQ.1 .AND. IBL.EQ.3 .AND. JS.EQ.2
     &           .AND. JBL.GE.85 .AND. JBL.LE.92) THEN
               WRITE(0,'(A,I3,A,Z8,A,Z8,A,Z8,A,Z8,A,Z8)')
     &          'F_WK_MASS JBL=',JBL,
     &          ' MASS=',TRANSFER(MASS(JBL,JS),1),
     &          ' UEM=',TRANSFER(UE_M,1),
     &          ' CONT=',TRANSFER(UE_M*MASS(JBL,JS),1),
     &          ' DUI_B=',TRANSFER(DUI-UE_M*MASS(JBL,JS),1),
     &          ' DUI=',TRANSFER(DUI,1)
              ENDIF
              IF(IS.EQ.1 .AND. IBL.EQ.9) THEN
               WRITE(0,'(A,I2,A,I3,A,I4,A,I4,A,Z8,A,Z8,A,Z8)')
     &          'F_DUI9 JS=',JS,' JBL=',JBL,
     &          ' I=',I,' J=',J,
     &          ' MASS=',TRANSFER(MASS(JBL,JS),1),
     &          ' UEM=',TRANSFER(UE_M,1),
     &          ' DUI=',TRANSFER(DUI,1)
              ENDIF
              IF(IS.EQ.1 .AND. (IBL.EQ.3 .OR. IBL.EQ.6)) THEN
               WRITE(0,'(A,I2,A,I3,A,I4,A,I4,A,Z8)')
     &          'F_DUI_T JS=',JS,' JBL=',JBL,
     &          ' I=',I,' J=',J,' DUI=',TRANSFER(DUI,1)
               IF(JS.EQ.2 .AND. (JBL.EQ.81 .OR.
     &             (JBL.GE.43 .AND. JBL.LE.46))) THEN
                WRITE(0,'(A,I3,A,Z8,A,Z8,A,Z8,A,Z8)')
     &           'F_DUI_D JBL=',JBL,
     &           ' MASS=',TRANSFER(MASS(JBL,JS),1),
     &           ' DIJ=',TRANSFER(DIJ(I,J),1),
     &           ' UEM=',TRANSFER(UE_M,1),
     &           ' CONT=',TRANSFER(UE_M*MASS(JBL,JS),1)
                IF(JBL.EQ.81) THEN
                  WRITE(0,'(A,Z8,A,Z8)')
     &             'F_WK81 DSTR=',TRANSFER(DSTR(JBL,JS),1),
     &             ' UEI=',TRANSFER(UEDG(JBL,JS),1)
                ENDIF
               ENDIF
              ENDIF
              IF(IS.EQ.2 .AND. IBL.EQ.3) THEN
               WRITE(0,'(A,I2,A,I3,A,I4,A,I4,A,Z8,A,Z8,A,Z8,A,Z8,A,Z8)')
     &          'F_DUI23 JS=',JS,' JBL=',JBL,
     &          ' I=',I,' J=',J,
     &          ' MASS=',TRANSFER(MASS(JBL,JS),1),
     &          ' UEM=',TRANSFER(UE_M,1),
     &          ' CONT=',TRANSFER(UE_M*MASS(JBL,JS),1),
     &          ' DUI_B=',TRANSFER(DUI-UE_M*MASS(JBL,JS),1),
     &          ' DUI=',TRANSFER(DUI,1)
              ENDIF
              IF(IS.EQ.2 .AND. IBL.EQ.2) THEN
               WRITE(0,'(A,I2,A,I3,A,I4,A,I4,A,Z8,A,Z8,A,Z8,A,Z8,A,Z8)')
     &          'F_DUI22 JS=',JS,' JBL=',JBL,
     &          ' I=',I,' J=',J,
     &          ' MASS=',TRANSFER(MASS(JBL,JS),1),
     &          ' UEM=',TRANSFER(UE_M,1),
     &          ' CONT=',TRANSFER(UE_M*MASS(JBL,JS),1),
     &          ' DUI_B=',TRANSFER(DUI-UE_M*MASS(JBL,JS),1),
     &          ' DUI=',TRANSFER(DUI,1)
              ENDIF
              IF(J .LE. N) THEN
               DUIA = DUIA + UE_M*MASS(JBL,JS)
               CALL TRACE_PREDICTED_EDGE_VELOCITY_TERM('UESET',
     &              IS, IBL, JS, JBL, I, J,
     &              VTI(IBL,IS), VTI(JBL,JS), DIJ(I,J),
     &              MASS(JBL,JS), UE_M, UE_M*MASS(JBL,JS), 0)
              ELSE
               DUIW = DUIW + UE_M*MASS(JBL,JS)
               CALL TRACE_PREDICTED_EDGE_VELOCITY_TERM('UESET',
     &              IS, IBL, JS, JBL, I, J,
     &              VTI(IBL,IS), VTI(JBL,JS), DIJ(I,J),
     &              MASS(JBL,JS), UE_M, UE_M*MASS(JBL,JS), 1)
              ENDIF
 1000       CONTINUE
          IF(IS.EQ.2 .AND. IBL.EQ.29) THEN
           WRITE(0,'(A,I2,A,Z8)')
     &      'F_DUI29_END JS=',JS,' DUI=',TRANSFER(DUI,1)
          ENDIF
  100     CONTINUE
C
C---- GDB: per-term DUI trace for IS=1 IBL=3
          IF(IS.EQ.1 .AND. IBL.EQ.3) THEN
           WRITE(0,'(A,I3,A,Z8)')
     &      'F_UESET_TERM N=',N,' DUI_FINAL=',TRANSFER(DUI,1)
          ENDIF
          UEDG(IBL,IS) = UINV(IBL,IS) + DUI
C
C---- GDB parity: dump UESET accumulators in hex
      WRITE(0,'(A,I2,A,I3,A,Z8,A,Z8,A,Z8)')
     & 'F_UESET IS=',IS,' IBL=',IBL,
     & ' UINV=',TRANSFER(UINV(IBL,IS),1),
     & ' DUI=',TRANSFER(DUI,1),
     & ' UEDG=',TRANSFER(UEDG(IBL,IS),1)
C
          CALL TRACE_REALHEX(UINV(IBL,IS), UINVBITS)
          CALL TRACE_REALHEX(DUIA, DUIABITS)
          CALL TRACE_REALHEX(DUIW, DUIWBITS)
          CALL TRACE_REALHEX(UEDG(IBL,IS), USAVBITS)
          WRITE(50,9901) IS, IBL, UINV(IBL,IS), UINVBITS,
     &                    DUIA, DUIABITS, DUIW, DUIWBITS,
     &                    UEDG(IBL,IS), USAVBITS
          WRITE(TRLINE,9901) IS, IBL, UINV(IBL,IS), UINVBITS,
     &                        DUIA, DUIABITS, DUIW, DUIWBITS,
     &                        UEDG(IBL,IS), USAVBITS
          CALL TRACE_TEXT('UESET', 'usav_split', TRLINE)
          CALL TRACE_PREDICTED_EDGE_VELOCITY('UESET',
     &         IS, IBL, UINV(IBL,IS), DUIA, DUIW, UEDG(IBL,IS))
C
   10   CONTINUE
    1 CONTINUE
C
      CALL TRACE_EXIT('UESET')
      RETURN
 9901 FORMAT('USAV_SPLIT IS=',I2,' IBL=',I4,' UINV=',1PE15.8,
     &       ' [',A8,']',' AIR=',1PE15.8,' [',A8,']',
     &       ' WAKE=',1PE15.8,' [',A8,']',' USAV=',1PE15.8,
     &       ' [',A8,']')
      END


      SUBROUTINE DSSET
      INCLUDE 'XFOIL.INC'
C
      DO 1 IS=1, 2
        DO 10 IBL=2, NBL(IS)
          DSTR(IBL,IS) = MASS(IBL,IS) / UEDG(IBL,IS)
   10   CONTINUE
    1 CONTINUE
C
      RETURN
      END
