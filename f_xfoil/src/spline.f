C***********************************************************************
C    Module:  spline.f
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

      SUBROUTINE SPLINE(X,XS,S,N)
      DIMENSION X(N),XS(N),S(N)
      PARAMETER (NMAX=1000)
      DIMENSION A(NMAX),B(NMAX),C(NMAX)
C-------------------------------------------------------
C     Calculates spline coefficients for X(S).          |
C     Zero 2nd derivative end conditions are used.      |
C     To evaluate the spline at some value of S,        |
C     use SEVAL and/or DEVAL.                           |
C                                                       |
C     S        independent variable array (input)       |
C     X        dependent variable array   (input)       |
C     XS       dX/dS array                (calculated)  |
C     N        number of points           (input)       |
C                                                       |
C-------------------------------------------------------
      IF(N.GT.NMAX) STOP 'SPLINE: array overflow, increase NMAX'
C     
      DO 1 I=2, N-1
        DSM = S(I) - S(I-1)
        DSP = S(I+1) - S(I)
        B(I) = DSP
        A(I) = 2.0*(DSM+DSP)
        C(I) = DSM
        XS(I) = 3.0*((X(I+1)-X(I))*DSM/DSP + (X(I)-X(I-1))*DSP/DSM)
    1 CONTINUE
C
C---- set zero second derivative end conditions
      A(1) = 2.0
      C(1) = 1.0
      XS(1) = 3.0*(X(2)-X(1)) / (S(2)-S(1))
      B(N) = 1.0
      A(N) = 2.0
      XS(N) = 3.0*(X(N)-X(N-1)) / (S(N)-S(N-1))
C
C---- solve for derivative array XS
      CALL TRISOL(A,B,C,XS,N)
C
      RETURN
      END ! SPLINE      


      SUBROUTINE SPLIND(X,XS,S,N,XS1,XS2)
      DIMENSION X(N),XS(N),S(N)
      PARAMETER (NMAX=1000)
      DIMENSION  A(NMAX),B(NMAX),C(NMAX)
      CHARACTER*32 STARTBC, ENDBC, PRECISION
      REAL LOWERI, UPPERI
C-------------------------------------------------------
C     Calculates spline coefficients for X(S).          |
C     Specified 1st derivative and/or usual zero 2nd    |
C     derivative end conditions are used.               |
C     To evaluate the spline at some value of S,        |
C     use SEVAL and/or DEVAL.                           |
C                                                       |
C     S        independent variable array (input)       |
C     X        dependent variable array   (input)       |
C     XS       dX/dS array                (calculated)  |
C     N        number of points           (input)       |
C     XS1,XS2  endpoint derivatives       (input)       |
C              If = 999.0, then usual zero second       |
C              derivative end condition(s) are used     |
C              If = -999.0, then zero third             |
C              derivative end condition(s) are used     |
C                                                       |
C-------------------------------------------------------
      IF(N.GT.NMAX) STOP 'SPLIND: array overflow, increase NMAX'
C     
      DO 1 I=2, N-1
        DSM = S(I) - S(I-1)
        DSP = S(I+1) - S(I)
        B(I) = DSP
        A(I) = 2.0*(DSM+DSP)
        C(I) = DSM
        XS(I) = 3.0*((X(I+1)-X(I))*DSM/DSP + (X(I)-X(I-1))*DSP/DSM)
    1 CONTINUE
C
      IF(XS1.EQ.999.0) THEN
C----- set zero second derivative end condition
       A(1) = 2.0
       C(1) = 1.0
       XS(1) = 3.0*(X(2)-X(1)) / (S(2)-S(1))
      ELSE IF(XS1.EQ.-999.0) THEN
C----- set zero third derivative end condition
       A(1) = 1.0
       C(1) = 1.0
       XS(1) = 2.0*(X(2)-X(1)) / (S(2)-S(1))
      ELSE
C----- set specified first derivative end condition
       A(1) = 1.0
       C(1) = 0.
       XS(1) = XS1
      ENDIF
C
      IF(XS2.EQ.999.0) THEN
       B(N) = 1.0
       A(N) = 2.0
       XS(N) = 3.0*(X(N)-X(N-1)) / (S(N)-S(N-1))
      ELSE IF(XS2.EQ.-999.0) THEN
       B(N) = 1.0
       A(N) = 1.0
       XS(N) = 2.0*(X(N)-X(N-1)) / (S(N)-S(N-1))
      ELSE
       A(N) = 1.0
       B(N) = 0.
       XS(N) = XS2
      ENDIF
C
      PRECISION = 'Single'
      IF(XS1.EQ.999.0) THEN
       STARTBC = 'ZeroSecondDerivative'
      ELSE IF(XS1.EQ.-999.0) THEN
       STARTBC = 'ZeroThirdDerivative'
      ELSE
       STARTBC = 'SpecifiedFirstDerivative'
      ENDIF
C
      IF(XS2.EQ.999.0) THEN
       ENDBC = 'ZeroSecondDerivative'
      ELSE IF(XS2.EQ.-999.0) THEN
       ENDBC = 'ZeroThirdDerivative'
      ELSE
       ENDBC = 'SpecifiedFirstDerivative'
      ENDIF
C
      IF(N.EQ.2 .AND. XS1.EQ.-999.0 .AND. XS2.EQ.-999.0) THEN
       B(N) = 1.0
       A(N) = 2.0
       XS(N) = 3.0*(X(N)-X(N-1)) / (S(N)-S(N-1))
      ENDIF
C
      DO 5 I=1, N
        LOWERI = 0.0
        UPPERI = 0.0
        IF(I.GT.1) LOWERI = B(I)
        IF(I.LT.N) UPPERI = C(I)
        CALL TRACE_SPLINE_SYSTEM_ROW('ParametricSpline', 'SPLIND',
     &       I, X(I), S(I), LOWERI, A(I), UPPERI, XS(I),
     &       STARTBC, ENDBC, PRECISION)
    5 CONTINUE
C
C---- solve for derivative array XS
      CALL TRISOL(A,B,C,XS,N)
C
      DO 6 I=1, N
        CALL TRACE_SPLINE_SOLUTION_NODE('ParametricSpline', 'SPLIND',
     &       I, X(I), S(I), XS(I), STARTBC, ENDBC, PRECISION)
    6 CONTINUE
C
      RETURN
      END ! SPLIND

 

      SUBROUTINE SPLINA(X,XS,S,N)
      IMPLICIT REAL (A-H,O-Z)
      DIMENSION X(N),XS(N),S(N)
      LOGICAL LEND
C-------------------------------------------------------
C     Calculates spline coefficients for X(S).          |
C     A simple averaging of adjacent segment slopes     |
C     is used to achieve non-oscillatory curve          |
C     End conditions are set by end segment slope       |
C     To evaluate the spline at some value of S,        |
C     use SEVAL and/or DEVAL.                           |
C                                                       |
C     S        independent variable array (input)       |
C     X        dependent variable array   (input)       |
C     XS       dX/dS array                (calculated)  |
C     N        number of points           (input)       |
C                                                       |
C-------------------------------------------------------
C     
      LEND = .TRUE.
      DO 1 I=1, N-1
        DS = S(I+1)-S(I)
        IF (DS.EQ.0.) THEN
          XS(I) = XS1
          LEND = .TRUE.
         ELSE
          DX = X(I+1)-X(I)
          XS2 = DX / DS
          IF (LEND) THEN
            XS(I) = XS2
            LEND = .FALSE.
           ELSE
            XS(I) = 0.5*(XS1 + XS2)
          ENDIF
        ENDIF
        XS1 = XS2
    1 CONTINUE
      XS(N) = XS1
C
      RETURN
      END ! SPLINA



      SUBROUTINE TRISOL(A,B,C,D,KK)
      DIMENSION A(KK),B(KK),C(KK),D(KK)
      CHARACTER*32 PRECISION
      REAL PIVOT, LOWER, UPPERBEFORE, RHSBEFOREPIVOT
      REAL DIAGONALBEFORE, RHSBEFORE, LASTPIVOT, LASTRHS
      REAL UPPER, NEXTVALUE
C-----------------------------------------
C     Solves KK long, tri-diagonal system |
C                                         |
C             A C          D              |
C             B A C        D              |
C               B A .      .              |
C                 . . C    .              |
C                   B A    D              |
C                                         |
C     The righthand side D is replaced by |
C     the solution.  A, C are destroyed.  |
C-----------------------------------------
C
      PRECISION = 'Single'
      DO 1 K=2, KK
        KM = K-1
        PIVOT = A(KM)
        LOWER = B(K)
        UPPERBEFORE = C(KM)
        RHSBEFOREPIVOT = D(KM)
        DIAGONALBEFORE = A(K)
        RHSBEFORE = D(K)
        C(KM) = C(KM) / A(KM)
        D(KM) = D(KM) / A(KM)
        A(K) = A(K) - B(K)*C(KM)
        D(K) = D(K) - B(K)*D(KM)
        CALL TRACE_TRIDIAGONAL_FORWARD('TridiagonalSolver',
     &       'TRISOL', K, PIVOT, LOWER, UPPERBEFORE,
     &       RHSBEFOREPIVOT, C(KM), D(KM), DIAGONALBEFORE,
     &       A(K), RHSBEFORE, D(K), PRECISION)
    1 CONTINUE
C
      LASTPIVOT = A(KK)
      LASTRHS = D(KK)
      D(KK) = D(KK)/A(KK)
      CALL TRACE_TRIDIAGONAL_LAST_PIVOT('TridiagonalSolver',
     &     'TRISOL', KK, LASTPIVOT, LASTRHS, D(KK), PRECISION)
C
      DO 2 K=KK-1, 1, -1
        RHSBEFORE = D(K)
        UPPER = C(K)
        NEXTVALUE = D(K+1)
        D(K) = D(K) - C(K)*D(K+1)
        CALL TRACE_TRIDIAGONAL_BACK('TridiagonalSolver',
     &       'TRISOL', K, UPPER, NEXTVALUE, RHSBEFORE, D(K),
     &       PRECISION)
    2 CONTINUE
C
      RETURN
      END ! TRISOL


      FUNCTION SEVAL(SS,X,XS,S,N)
      DIMENSION X(N), XS(N), S(N)
      CHARACTER*32 PRECISION
      REAL XLOW, XHIGH, XSLOW, XSHIGH, ONEMT, LINLOW, LINHIGH
      REAL CUBFAC, PROD1, PROD2, CUBIC
C--------------------------------------------------
C     Calculates X(SS)                             |
C     XS array must have been calculated by SPLINE |
C--------------------------------------------------
      PRECISION = 'Single'
      ILOW = 1
      I = N
C
   10 IF(I-ILOW .LE. 1) GO TO 11
C
      IMID = (I+ILOW)/2
      IF(SS .LT. S(IMID)) THEN
       I = IMID
      ELSE
       ILOW = IMID
      ENDIF
      GO TO 10
C
   11 DS = S(I) - S(I-1)
      T = (SS - S(I-1)) / DS
      XLOW = X(I-1)
      XHIGH = X(I)
      XSLOW = XS(I-1)
      XSHIGH = XS(I)
      ONEMT = 1.0 - T
      CX1 = DS*XSLOW - XHIGH + XLOW
      CX2 = DS*XSHIGH - XHIGH + XLOW
      LINLOW = ONEMT*XLOW
      LINHIGH = T*XHIGH
      CUBFAC = T - T*T
      PROD1 = ONEMT*CX1
      PROD2 = T*CX2
      CUBIC = CUBFAC*(PROD1 - PROD2)
      SEVAL = LINHIGH + LINLOW + CUBIC
      CALL TRACE_SPLINE_EVAL('ParametricSpline', 'SEVAL', I-1, I,
     &     SS, DS, T, XLOW, XHIGH, XSLOW, XSHIGH, CX1, CX2,
     &     XHIGH-XLOW, CUBFAC, ONEMT, LINLOW, LINHIGH,
     &     PROD1, PROD2, PROD1-PROD2, CUBIC, SEVAL, PRECISION)
      RETURN
      END ! SEVAL

      FUNCTION DEVAL(SS,X,XS,S,N)
      DIMENSION X(N), XS(N), S(N)
      CHARACTER*32 PRECISION
      REAL XLOW, XHIGH, XSLOW, XSHIGH, DELTA, FAC1, FAC2
      REAL PROD1, PROD2, ACCUM
C--------------------------------------------------
C     Calculates dX/dS(SS)                         |
C     XS array must have been calculated by SPLINE |
C--------------------------------------------------
      PRECISION = 'Single'
      ILOW = 1
      I = N
C
   10 IF(I-ILOW .LE. 1) GO TO 11
C
      IMID = (I+ILOW)/2
      IF(SS .LT. S(IMID)) THEN
       I = IMID
      ELSE
       ILOW = IMID
      ENDIF
      GO TO 10
C
   11 DS = S(I) - S(I-1)
      T = (SS - S(I-1)) / DS
      XLOW = X(I-1)
      XHIGH = X(I)
      XSLOW = XS(I-1)
      XSHIGH = XS(I)
      CX1 = DS*XSLOW - XHIGH + XLOW
      CX2 = DS*XSHIGH - XHIGH + XLOW
      DELTA = XHIGH - XLOW
      FAC1 = 1. - 4.0*T + 3.0*T*T
      FAC2 = T*(3.0*T-2.)
      PROD1 = FAC1*CX1
      PROD2 = FAC2*CX2
      ACCUM = DELTA + PROD1 + PROD2
      DEVAL = ACCUM/DS
      CALL TRACE_SPLINE_EVAL('ParametricSpline', 'DEVAL', I-1, I,
     &     SS, DS, T, XLOW, XHIGH, XSLOW, XSHIGH, CX1, CX2,
     &     DELTA, FAC1, FAC2, PROD1, PROD2, PROD1, PROD2,
     &     PROD1+PROD2, ACCUM, DEVAL, PRECISION)
      RETURN
      END ! DEVAL

      FUNCTION D2VAL(SS,X,XS,S,N)
      DIMENSION X(N), XS(N), S(N)
C--------------------------------------------------
C     Calculates d2X/dS2(SS)                       |
C     XS array must have been calculated by SPLINE |
C--------------------------------------------------
      ILOW = 1
      I = N
C
   10 IF(I-ILOW .LE. 1) GO TO 11
C
      IMID = (I+ILOW)/2
      IF(SS .LT. S(IMID)) THEN
       I = IMID
      ELSE
       ILOW = IMID
      ENDIF
      GO TO 10
C
   11 DS = S(I) - S(I-1)
      T = (SS - S(I-1)) / DS
      CX1 = DS*XS(I-1) - X(I) + X(I-1)
      CX2 = DS*XS(I)   - X(I) + X(I-1)
      D2VAL = (6.*T-4.)*CX1 + (6.*T-2.0)*CX2
      D2VAL = D2VAL/DS**2
      RETURN
      END ! D2VAL


      FUNCTION CURV(SS,X,XS,Y,YS,S,N)
      DIMENSION X(N), XS(N), Y(N), YS(N), S(N)
      CHARACTER*32 PRECISION
C-----------------------------------------------
C     Calculates curvature of splined 2-D curve |
C     at S = SS                                 |
C                                               |
C     S        arc length array of curve        |
C     X, Y     coordinate arrays of curve       |
C     XS,YS    derivative arrays                |
C              (calculated earlier by SPLINE)   |
C-----------------------------------------------
C     
      PRECISION = 'Single'
      ILOW = 1
      I = N
C
   10 IF(I-ILOW .LE. 1) GO TO 11
C
      IMID = (I+ILOW)/2
      IF(SS .LT. S(IMID)) THEN
       I = IMID
      ELSE
       ILOW = IMID
      ENDIF
      GO TO 10
C
   11 DS = S(I) - S(I-1)
      T = (SS - S(I-1)) / DS
C
      CX1 = DS*XS(I-1) - X(I) + X(I-1)
      CX2 = DS*XS(I)   - X(I) + X(I-1)
      XD = X(I) - X(I-1) + (1.0-4.0*T+3.0*T*T)*CX1 + T*(3.0*T-2.0)*CX2
      XDD = (6.0*T-4.0)*CX1 + (6.0*T-2.0)*CX2
C
      CY1 = DS*YS(I-1) - Y(I) + Y(I-1)
      CY2 = DS*YS(I)   - Y(I) + Y(I-1)
      YD = Y(I) - Y(I-1) + (1.0-4.0*T+3.0*T*T)*CY1 + T*(3.0*T-2.0)*CY2
      YDD = (6.0*T-4.0)*CY1 + (6.0*T-2.0)*CY2
C 
      XDEL = X(I) - X(I-1)
      XFAC1 = 1.0 - 4.0*T + 3.0*T*T
      XFAC2 = T*(3.0*T-2.0)
      XTERM1 = XFAC1*CX1
      XTERM2 = XFAC2*CX2
      YDEL = Y(I) - Y(I-1)
      YFAC1 = 1.0 - 4.0*T + 3.0*T*T
      YFAC2 = T*(3.0*T-2.0)
      YTERM1 = YFAC1*CY1
      YTERM2 = YFAC2*CY2
      SD = SQRT(XD*XD + YD*YD)
      SD = MAX(SD,0.001*DS)
C
      CURV = (XD*YDD - YD*XDD) / SD**3
C
      CALL TRACE_CURVATURE_EVAL('ParametricSpline', 'CURV', ILOW, I,
     &     SS, DS, T, XDEL, YDEL, CX1, CX2, CY1, CY2,
     &     XFAC1, XFAC2, YFAC1, YFAC2,
     &     XTERM1, XTERM2, YTERM1, YTERM2,
     &     XD, XDD, YD, YDD, SD, CURV, PRECISION)
C
      RETURN
      END ! CURV


      FUNCTION CURVS(SS,X,XS,Y,YS,S,N)
      DIMENSION X(N), XS(N), Y(N), YS(N), S(N)
C-----------------------------------------------
C     Calculates curvature derivative of        |
C     splined 2-D curve at S = SS               |
C                                               |
C     S        arc length array of curve        |
C     X, Y     coordinate arrays of curve       |
C     XS,YS    derivative arrays                |
C              (calculated earlier by SPLINE)   |
C-----------------------------------------------
C     
      ILOW = 1
      I = N
C
   10 IF(I-ILOW .LE. 1) GO TO 11
C
      IMID = (I+ILOW)/2
      IF(SS .LT. S(IMID)) THEN
       I = IMID
      ELSE
       ILOW = IMID
      ENDIF
      GO TO 10
C
   11 DS = S(I) - S(I-1)
      T = (SS - S(I-1)) / DS
C
      CX1 = DS*XS(I-1) - X(I) + X(I-1)
      CX2 = DS*XS(I)   - X(I) + X(I-1)
      XD = X(I) - X(I-1) + (1.0-4.0*T+3.0*T*T)*CX1 + T*(3.0*T-2.0)*CX2
      XDD = (6.0*T-4.0)*CX1 + (6.0*T-2.0)*CX2
      XDDD = 6.0*CX1 + 6.0*CX2
C
      CY1 = DS*YS(I-1) - Y(I) + Y(I-1)
      CY2 = DS*YS(I)   - Y(I) + Y(I-1)
      YD = Y(I) - Y(I-1) + (1.0-4.0*T+3.0*T*T)*CY1 + T*(3.0*T-2.0)*CY2
      YDD = (6.0*T-4.0)*CY1 + (6.0*T-2.0)*CY2
      YDDD = 6.0*CY1 + 6.0*CY2
C
      SD = SQRT(XD*XD + YD*YD)
      SD = MAX(SD,0.001*DS)
C
      BOT = SD**3
      DBOTDT = 3.0*SD*(XD*XDD + YD*YDD)
C
      TOP = XD*YDD - YD*XDD      
      DTOPDT = XD*YDDD - YD*XDDD
C
      CURVS = (DTOPDT*BOT - DBOTDT*TOP) / BOT**2
C
      RETURN
      END ! CURVS


      SUBROUTINE SINVRT(SI,XI,X,XS,S,N)
      DIMENSION X(N), XS(N), S(N)
C-------------------------------------------------------
C     Calculates the "inverse" spline function S(X).    |
C     Since S(X) can be multi-valued or not defined,    |
C     this is not a "black-box" routine.  The calling   |
C     program must pass via SI a sufficiently good      |
C     initial guess for S(XI).                          |
C                                                       |
C     XI      specified X value       (input)           |
C     SI      calculated S(XI) value  (input,output)    |
C     X,XS,S  usual spline arrays     (input)           |
C                                                       |
C-------------------------------------------------------
C
      SISAV = SI
C
      DO 10 ITER=1, 10
        RES  = SEVAL(SI,X,XS,S,N) - XI
        RESP = DEVAL(SI,X,XS,S,N)
        DS = -RES/RESP
        SI = SI + DS
        IF(ABS(DS/(S(N)-S(1))) .LT. 1.0E-5) RETURN
   10 CONTINUE
      WRITE(*,*)
     &  'SINVRT: spline inversion failed. Input value returned.'
      SI = SISAV
C
      RETURN
      END ! SINVRT


      SUBROUTINE SCALC(X,Y,S,N)
      DIMENSION X(N), Y(N), S(N)
      CHARACTER*32 PRECISION
C----------------------------------------
C     Calculates the arc length array S  |
C     for a 2-D array of points (X,Y).   |
C----------------------------------------
C
      PRECISION = 'Single'
      S(1) = 0.
      DO 10 I=2, N
        DX = X(I)-X(I-1)
        DY = Y(I)-Y(I-1)
        DS = SQRT(DX**2 + DY**2)
        S(I) = S(I-1) + DS
        CALL TRACE_ARC_LENGTH_STEP('ParametricSpline', 'SCALC', I,
     &       DX, DY, DS, S(I), PRECISION)
   10 CONTINUE
C
      RETURN
      END ! SCALC


      SUBROUTINE SPLNXY(X,XS,Y,YS,S,N)
      DIMENSION X(N), XS(N), Y(N), YS(N), S(N)
C-----------------------------------------
C     Splines 2-D shape X(S), Y(S), along |
C     with true arc length parameter S.   |
C-----------------------------------------
      PARAMETER (KMAX=32)
      DIMENSION XT(0:KMAX), YT(0:KMAX)
C
      KK = KMAX
      NPASS = 10
C
C---- set first estimate of arc length parameter
      CALL SCALC(X,Y,S,N)
C
C---- spline X(S) and Y(S)
      CALL SEGSPL(X,XS,S,N)
      CALL SEGSPL(Y,YS,S,N)
C
C---- re-integrate true arc length
      DO 100 IPASS=1, NPASS
C
        SERR = 0.
C
        DS = S(2) - S(1)
        DO I = 2, N
          DX = X(I) - X(I-1)
          DY = Y(I) - Y(I-1)
C
          CX1 = DS*XS(I-1) - DX
          CX2 = DS*XS(I  ) - DX
          CY1 = DS*YS(I-1) - DY
          CY2 = DS*YS(I  ) - DY
C
          XT(0) = 0.
          YT(0) = 0.
          DO K=1, KK-1
            T = FLOAT(K) / FLOAT(KK)
            XT(K) = T*DX + (T-T*T)*((1.0-T)*CX1 - T*CX2)
            YT(K) = T*DY + (T-T*T)*((1.0-T)*CY1 - T*CY2)
          ENDDO
          XT(KK) = DX
          YT(KK) = DY
C
          SINT1 = 0.
          DO K=1, KK
            SINT1 = SINT1
     &            + SQRT((XT(K)-XT(K-1))**2 + (YT(K)-YT(K-1))**2)
          ENDDO
C
          SINT2 = 0.
          DO K=2, KK, 2
            SINT2 = SINT2
     &            + SQRT((XT(K)-XT(K-2))**2 + (YT(K)-YT(K-2))**2)
          ENDDO
C
          SINT = (4.0*SINT1 - SINT2) / 3.0
C
          IF(ABS(SINT-DS) .GT. ABS(SERR))  SERR = SINT - DS
C
          IF(I.LT.N) DS = S(I+1) - S(I)
C
          S(I) = S(I-1) + SQRT(SINT)
        ENDDO
C
        SERR = SERR / (S(N) - S(1))
        WRITE(*,*) IPASS, SERR
C
C------ re-spline X(S) and Y(S)
        CALL SEGSPL(X,XS,S,N)
        CALL SEGSPL(Y,YS,S,N)
C
        IF(ABS(SERR) .LT. 1.0E-7) RETURN
C
 100  CONTINUE
C
      RETURN
      END ! SPLNXY



      SUBROUTINE SEGSPL(X,XS,S,N)
C-----------------------------------------------
C     Splines X(S) array just like SPLINE,      |
C     but allows derivative discontinuities     |
C     at segment joints.  Segment joints are    |
C     defined by identical successive S values. |
C-----------------------------------------------
      DIMENSION X(N), XS(N), S(N)
      CHARACTER*32 PRECISION
C
      IF(S(1).EQ.S(2)  ) STOP 'SEGSPL:  First input point duplicated'
      IF(S(N).EQ.S(N-1)) STOP 'SEGSPL:  Last  input point duplicated'
C
      PRECISION = 'Single'
      ISEG0 = 1
      DO 10 ISEG=2, N-2
        IF(S(ISEG).EQ.S(ISEG+1)) THEN
         NSEG = ISEG - ISEG0 + 1
         CALL TRACE_SPLINE_SEGMENT('ParametricSpline', 'SEGSPL',
     &        ISEG0, NSEG, 'ZeroThirdDerivative',
     &        'ZeroThirdDerivative', PRECISION)
         CALL SPLIND(X(ISEG0),XS(ISEG0),S(ISEG0),NSEG,-999.0,-999.0)
         ISEG0 = ISEG+1
        ENDIF
   10 CONTINUE
C
      NSEG = N - ISEG0 + 1
      CALL TRACE_SPLINE_SEGMENT('ParametricSpline', 'SEGSPL',
     &     ISEG0, NSEG, 'ZeroThirdDerivative',
     &     'ZeroThirdDerivative', PRECISION)
      CALL SPLIND(X(ISEG0),XS(ISEG0),S(ISEG0),NSEG,-999.0,-999.0)
C
      RETURN
      END ! SEGSPL



      SUBROUTINE SEGSPLD(X,XS,S,N,XS1,XS2)
C-----------------------------------------------
C     Splines X(S) array just like SPLIND,      |
C     but allows derivative discontinuities     |
C     at segment joints.  Segment joints are    |
C     defined by identical successive S values. |
C-----------------------------------------------
      DIMENSION X(N), XS(N), S(N)
C
      IF(S(1).EQ.S(2)  ) STOP 'SEGSPL:  First input point duplicated'
      IF(S(N).EQ.S(N-1)) STOP 'SEGSPL:  Last  input point duplicated'
C
      ISEG0 = 1
      DO 10 ISEG=2, N-2
        IF(S(ISEG).EQ.S(ISEG+1)) THEN
         NSEG = ISEG - ISEG0 + 1
         CALL SPLIND(X(ISEG0),XS(ISEG0),S(ISEG0),NSEG,XS1,XS2)
         ISEG0 = ISEG+1
        ENDIF
   10 CONTINUE
C
      NSEG = N - ISEG0 + 1
      CALL SPLIND(X(ISEG0),XS(ISEG0),S(ISEG0),NSEG,XS1,XS2)
C
      RETURN
      END ! SEGSPL
