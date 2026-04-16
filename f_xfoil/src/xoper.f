C***********************************************************************
C    Module:  xoper.f
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
      SUBROUTINE OPER
      INCLUDE 'XFOIL.INC'
      CHARACTER*1 ANS
      CHARACTER*4 COMAND, COMOLD
      LOGICAL LRECALC, LCPX
C
      CHARACTER*128 COMARG, ARGOLD, LINE
C
      PARAMETER (NPRX = 101)
      DIMENSION XPR(NPRX), YPR(NPRX), FPR(NPRX)
C
      DIMENSION NBLP(NPX)
      DIMENSION IPPAI(NPX), NAPOLT(NPX)
C
      DIMENSION IINPUT(20)
      DIMENSION RINPUT(20)
      LOGICAL ERROR
C
C---- retain last-command info if OPER is exited and then re-entered
      SAVE COMOLD, ARGOLD
C
C---- logical units for  polar save file,  polar dump file
      LUPLR = 9
      LUPLX = 11
C
      COMAND = '****'
      COMARG = ' '
      LRECALC = .FALSE.
      LCPX = .FALSE.
C
      IF(N.EQ.0) THEN
       WRITE(*,*)
       WRITE(*,*) '***  No airfoil available  ***'
       RETURN
      ENDIF
C
      IF(IPACT.NE.0) THEN
       WRITE(*,5000) IPACT
 5000  FORMAT(/'  Polar', I3,'  is active')
      ENDIF
C
ccc 500  CONTINUE
      COMOLD = COMAND
      ARGOLD = COMARG
C
C====================================================
C---- start of menu loop
 500  CONTINUE
C
      IF(LVISC) THEN
        IF(LPACC) THEN
         CALL ASKC('.OPERva^',COMAND,COMARG)
        ELSE
         CALL ASKC('.OPERv^',COMAND,COMARG)
        ENDIF
      ELSE
        IF(LPACC) THEN
         CALL ASKC('.OPERia^',COMAND,COMARG)
        ELSE
         CALL ASKC('.OPERi^',COMAND,COMARG)
        ENDIF
      ENDIF
C
C---- process previous command ?
      IF(COMAND(1:1).EQ.'!') THEN
        IF(COMOLD.EQ.'****') THEN
          WRITE(*,*) 'Previous .OPER command not valid'
          GO TO 500
        ELSE
          COMAND = COMOLD
          COMARG = ARGOLD
          LRECALC = .TRUE.
        ENDIF
      ELSE
        LRECALC = .FALSE.
      ENDIF
C
      IF(COMAND.EQ.'    ') THEN
C----- just <return> was typed... clean up plotting and exit OPER
       IF(LPLOT) CALL PLEND
       LPLOT = .FALSE.
       CALL CLRZOOM
       RETURN
      ENDIF
C
C---- extract command line numeric arguments
      DO I=1, 20
        IINPUT(I) = 0
        RINPUT(I) = 0.0
      ENDDO
      NINPUT = 20
      CALL GETFLT(COMARG,RINPUT,NINPUT,ERROR)
C
C---- don't try to read integers, since might get integer overflow
      DO I=1, NINPUT
        IF(ABS(RINPUT(I)) .GT. 2.1E9) THEN
         IINPUT(I) = 2**30
        ELSE
         IINPUT(I) = INT(RINPUT(I))
        ENDIF
      ENDDO
C
ccc      NINPUT = 20
ccc      CALL GETINT(COMARG,IINPUT,NINPUT,ERROR)
C
C--------------------------------------------------------
      IF(COMAND.EQ.'?   ') THEN
       WRITE(*,1050)
 1050  FORMAT(
     & /'   <cr>     Return to Top Level'
     & /'   !        Redo last ALFA,CLI,CL,ASEQ,CSEQ,VELS'
     &//'   Visc r   Toggle Inviscid/Viscous mode'
     & /'  .VPAR     Change BL parameter(s)'
     & /'   Re   r   Change Reynolds number'
     & /'   Mach r   Change Mach number'
     & /'   Type i   Change type of Mach,Re variation with CL'
     & /'   ITER     Change viscous-solution iteration limit'
     & /'   INIT     Toggle BL initialization flag'
     &//'   Alfa r   Prescribe alpha'
     & /'   CLI  r   Prescribe inviscid CL'
     & /'   Cl   r   Prescribe CL'
     & /'   ASeq rrr Prescribe a sequence of alphas'
     & /'   CSeq rrr Prescribe a sequence of CLs'
     &//'   SEQP     Toggle polar/Cp(x) sequence plot display'
     & /'   CINC     Toggle  minimum Cp  inclusion in polar'
     & /'   HINC     Toggle hinge moment inclusion in polar'
     & /'   Pacc i   Toggle auto point accumulation to active polar'
     & /'   PGET f   Read new polar from save file'
     & /'   PWRT i   Write polar to save file'
     & /'   PSUM     Show summary of stored polars'
     & /'   PLIS i   List stored polar(s)'
     & /'   PDEL i   Delete stored polar'
     & /'   PSOR i   Sort stored polar'
     & /'   PPlo ii. Plot stored polar(s)'
     & /'   APlo ii. Plot stored airfoil(s) for each polar'
     & /'   ASET i   Copy stored airfoil into current airfoil'
     & /'   PREM ir. Remove point(s) from stored polar'
     & /'   PNAM i   Change airfoil name of stored polar'
     & /'   PPAX     Change polar plot axis limits'
     &//'   RGET f   Read new reference polar from file'
     & /'   RDEL i   Delete stored reference polar'
     &//'   GRID     Toggle Cp vs x grid overlay'
     & /'   CREF     Toggle reference Cp data overlay'
     & /'   FREF     Toggle reference CL,CD.. data display'
     &//'   CPx      Plot Cp vs x'
     & /'   CPV      Plot airfoil with pressure vectors (gee wiz)'
     & /'  .VPlo     BL variable plots'
     & /'  .ANNO     Annotate current plot'
     & /'   HARD     Hardcopy current plot'
     & /'   SIZE r   Change plot-object size'
     & /'   CPMI r   Change minimum Cp axis annotation'
     &//'   BL   i   Plot boundary layer velocity profiles'
     & /'   BLC      Plot boundary layer velocity profiles at cursor'
     & /'   BLWT r   Change velocity profile scale weight'
     &//'   FMOM     Calculate flap hinge moment and forces'
     & /'   FNEW rr  Set new flap hinge point'
     & /'   VELS rr  Calculate velocity components at a point'
     & /'   DUMP f   Output Ue,Dstar,Theta,Cf vs s,x,y to file'
     & /'   CPWR f   Output x vs Cp to file'
     & /'   CPMN     Report minimum surface Cp'
     & /'   NAME s   Specify new airfoil name'
     & /'   NINC     Increment name version number')
c     &//'   IMAG    Toggle image-airfoil'
C
C--------------------------------------------------------
      ELSEIF(COMAND.EQ.'Z   ') THEN
       CALL USETZOOM(.TRUE.,.TRUE.)
       CALL REPLOT(IDEV)
C
C--------------------------------------------------------
      ELSEIF(COMAND.EQ.'U   ') THEN
       CALL CLRZOOM
       CALL REPLOT(IDEV)
C
C--------------------------------------------------------
      ELSEIF(COMAND.EQ.'VISC' .OR.
     &       COMAND.EQ.'V   '      ) THEN
       IF(LPACC) THEN
        WRITE(*,2100)
        GO TO 500
       ENDIF
C
       LVISC = .NOT. LVISC
C
       IF(LVISC) THEN
         IF(NINPUT.GE.1) THEN
           REINF1 = RINPUT(1)
         ELSE IF(REINF1 .EQ. 0.0) THEN
           CALL ASKR('Enter Reynolds number^',REINF1)
         ENDIF
C
         CALL MRSHOW(.TRUE.,.TRUE.)
       ENDIF
       LVCONV = .FALSE.
C
C--------------------------------------------------------
      ELSEIF(COMAND.EQ.'HARD') THEN
       IF(LPLOT) CALL PLEND
       LPLOT = .FALSE.
       CALL REPLOT(IDEVRP)
C
C--------------------------------------------------------
      ELSEIF(COMAND.EQ.'SIZE') THEN
       IF(NINPUT.GE.1) THEN
        SIZE = RINPUT(1)
       ELSE
        WRITE(*,*) 'Current plot-object size =', SIZE
        CALL ASKR('Enter new plot-object size^',SIZE)
       ENDIF
C
C--------------------------------------------------------
      ELSEIF(COMAND.EQ.'CPMI') THEN
       IF(NINPUT.GE.1) THEN
        CPMIN = RINPUT(1)
       ELSE
        WRITE(*,*) 'Current CPmin =', CPMIN
        CALL ASKR('Enter new CPmin^',CPMIN)
       ENDIF
C
       PFAC = PLOTAR/(CPMAX-CPMIN)
       CPDEL = -0.5
       IF(CPMIN .LT. -4.01) CPDEL = -1.0
C
C--------------------------------------------------------
      ELSEIF(COMAND.EQ.'UEMA') THEN
       IF(NINPUT.GE.1) THEN
        UEMAX = RINPUT(1)
       ELSE
        WRITE(*,*) 'Current UEmax =', UEMAX
        CALL ASKR('Enter new UEMAX^',UEMAX)
       ENDIF
C
       UFAC = PLOTAR/(UEMAX-UEMIN)
       UEDEL = 0.2
       IF((UEMAX-UEMIN) .GT. 2.51) UEDEL = 0.5
       IF((UEMAX-UEMIN) .GT. 5.01) UEDEL = 1.0
C
C--------------------------------------------------------
      ELSEIF(COMAND.EQ.'UEMI') THEN
       IF(NINPUT.GE.1) THEN
        UEMIN = RINPUT(1)
       ELSE
        WRITE(*,*) 'Current UEmin =', UEMIN
        CALL ASKR('Enter new UEMIN^',UEMIN)
       ENDIF
C
       UFAC = PLOTAR/(UEMAX-UEMIN)
       UEDEL = 0.2
       IF((UEMAX-UEMIN) .GT. 2.51) UEDEL = 0.5
       IF((UEMAX-UEMIN) .GT. 5.01) UEDEL = 1.0
C
C--------------------------------------------------------
      ELSEIF(COMAND.EQ.'VPAR') THEN
       CALL VPAR
C
C--------------------------------------------------------
      ELSEIF(COMAND.EQ.'RE  ' .OR.
     &       COMAND.EQ.'R   '      ) THEN
       IF(LPACC .AND. LVISC) THEN
        WRITE(*,2100)
        GO TO 500
       ENDIF
C
       IF(NINPUT.GE.1) THEN
        REINF1 = RINPUT(1)
       ELSE
        WRITE(*,*)
        WRITE(*,*) 'Currently...'
        CALL MRSHOW(.FALSE.,.TRUE.)
        CALL ASKR('Enter new Reynolds number^',REINF1)
       ENDIF
C
ccc    CALL MRSHOW(.FALSE.,.TRUE.)
       CALL MRCL(1.0,MINF_CL,REINF_CL)
       LVCONV = .FALSE.
C
C--------------------------------------------------------
      ELSEIF(COMAND.EQ.'MACH' .OR.
     &       COMAND.EQ.'M   '      ) THEN
       IF(LPACC) THEN
        WRITE(*,2100)
        GO TO 500
       ENDIF
C
 15    CONTINUE
       IF(NINPUT.GE.1) THEN
        MINF1 = RINPUT(1)
       ELSE
        WRITE(*,*)
        WRITE(*,*) 'Currently...'
        CALL MRSHOW(.TRUE.,.FALSE.)
        CALL ASKR('Enter Mach number^',MINF1)
       ENDIF
C
       IF(MINF1.GE.1.0) THEN
        WRITE(*,*) 'Supersonic freestream not allowed'
        NINPUT = 0
        GO TO 15
       ENDIF
ccc    CALL MRSHOW(.TRUE.,.FALSE.)
       CALL MRCL(1.0,MINF_CL,REINF_CL)
       CALL COMSET
C
       IF(MINF.GT.0.0) WRITE(*,1300) CPSTAR, QSTAR/QINF
 1300  FORMAT(/' Sonic Cp =', F10.2, '      Sonic Q/Qinf =', F10.3/)
C
       CALL CPCALC(N,QINV,QINF,MINF,CPI)
       IF(LVISC) CALL CPCALC(N+NW,QVIS,QINF,MINF,CPV)
       CALL CLCALC(N,X,Y,GAM,GAM_A,ALFA,MINF,QINF, XCMREF,YCMREF,
     &             CL,CM,CDP, CL_ALF,CL_MSQ)
       CALL CDCALC
       LVCONV = .FALSE.
C
C--------------------------------------------------------
      ELSEIF(COMAND.EQ.'TYPE' .OR.
     &       COMAND.EQ.'T'         ) THEN
       IF(LPACC) THEN
        WRITE(*,2100)
        GO TO 500
       ENDIF
C
   17  CONTINUE
       IF(NINPUT.GE.1) THEN
        ITYP = IINPUT(1)
       ELSE
        WRITE(*,1105)
 1105   FORMAT(
     & /' Type   parameters held constant       varying      fixed   '
     & /' ----   ------------------------       -------   -----------'
     & /'   1    M          , Re            ..   lift     chord, vel.'
     & /'   2    M sqrt(CL) , Re sqrt(CL)   ..   vel.     chord, lift'
     & /'   3    M          , Re CL         ..   chord    lift , vel.')
        CALL ASKI('Enter type of Mach,Re variation with CL^',ITYP)
       ENDIF
C
       IF(ITYP.EQ.1) THEN
        MATYP = 1
        RETYP = 1
       ELSE IF(ITYP.EQ.2) THEN
        MATYP = 2
        RETYP = 2
       ELSE IF(ITYP.EQ.3) THEN
        MATYP = 1
        RETYP = 3
       ENDIF
C
       IF(ITYP.LT.1 .OR. ITYP.GT.3) THEN
        NINPUT = 0
        GO TO 17
       ENDIF
C
       CALL MRSHOW(.TRUE.,.TRUE.)
       CALL MRCL(1.0,MINF_CL,REINF_CL)
       CALL COMSET
       LVCONV = .FALSE.
C
C--------------------------------------------------------
      ELSEIF(COMAND.EQ.'ITER') THEN
 18    CONTINUE
       IF(NINPUT.GE.1) THEN
        ITMAX = IINPUT(1)
       ELSE
        WRITE(*,*) 'Current iteration limit:', ITMAX
        CALL ASKI('Enter new iteration limit^',ITMAX)
       ENDIF
C
       IF(ITMAX.LT.1) THEN
        NINPUT = 0
        GO TO 18
       ENDIF
C
C--------------------------------------------------------
      ELSEIF(COMAND.EQ.'INIT') THEN
       LBLINI = .NOT.LBLINI
       IF(LBLINI) THEN
        WRITE(*,*) 'BLs are assumed to be initialized'
       ELSE
        WRITE(*,*) 'BLs will be initialized on next point'
        LIPAN = .FALSE.
       ENDIF
C
C--------------------------------------------------------
c      ELSEIF(COMAND.EQ.'IMAG') THEN
c       LIMAGE = .NOT.LIMAGE
c       IF(LIMAGE) THEN
c        CALL ASKR('Enter y-position of image plane^',YIMAGE)
c        CALL ASKI('Specify image type (1=wall -1=free jet)^',KIMAGE)
c       ELSE
c        WRITE(*,*) 'Image airfoil removed'
c       ENDIF
c       LGAMU = .FALSE.
c       LQAIJ = .FALSE.
C
C--------------------------------------------------------
      ELSEIF(COMAND.EQ.'ALFA' .OR.
     &       COMAND.EQ.'A   '     ) THEN
       IF(.NOT.LRECALC) THEN
C------- set inviscid solution only if point is not being recalculated
         IF(NINPUT.GE.1) THEN
          ADEG = RINPUT(1)
         ELSE
          ADEG = ALFA/DTOR          
          CALL ASKR('Enter angle of attack (deg)^',ADEG)
         ENDIF
         LALFA = .TRUE.
         ALFA = DTOR*ADEG
         QINF = 1.0
         CALL SPECAL
         IF(ABS(ALFA-AWAKE) .GT. 1.0E-5) LWAKE  = .FALSE.
         IF(ABS(ALFA-AVISC) .GT. 1.0E-5) LVCONV = .FALSE.
         IF(ABS(MINF-MVISC) .GT. 1.0E-5) LVCONV = .FALSE.
       ENDIF
C
       CALL SET_NCRIT_FROM_ENV
       WRITE(0,*) 'PRE_VISCAL ACRIT=', ACRIT
       IF(LVISC) THEN
         CALL VISCAL(ITMAX)
       ENDIF
ccc    CALL CPX (skipped: triggers X11)
       CALL FCPMIN
C
ccc    IF( LVISC .AND. LPACC .AND. LVCONV ) THEN
       IF( LPACC .AND. (LVCONV .OR. .NOT.LVISC)) THEN
        CALL PLRADD(LUPLR,IPACT)
        CALL PLXADD(LUPLX,IPACT)
       ENDIF
C
       IF(LVISC .AND. .NOT.LPACC .AND. .NOT.LVCONV) THEN
        WRITE(*,*) 'Type "!" to continue iterating'
       ENDIF
C
C      WRITE(*,*) 'N NW =', N, NW
C      call aski('Enter i^',ioff)
C      call askr('Enter dmass^',dms)
Cc
C      do 43 is=1, 2
C        do 430 ibl=2, nbl(is)
C          i = ipan(ibl,is)
C          mass(ibl,is) = 0.
C          if(i.eq.ioff) mass(ibl,is) = dms
C  430   continue
C   43 continue
Cc
C      call ueset
C      call qvfue
C      call gamqv
C      call cpcalc(N+NW,QVIS,QINF,MINF,CPV)
C      call cdcalc
c      CALL CLCALC(N,X,Y,GAM,GAM_A,ALFA,MINF,QINF,XCMREF,YCMREF,
c     &            CL,CM,CDP, CL_ALF,CL_MSQ)
C      call cpx
Cc
C
       COMOLD = COMAND
       ARGOLD = COMARG
C
C--------------------------------------------------------
      ELSEIF(COMAND.EQ.'CLI ') THEN
       IF(.NOT.LRECALC) THEN
        IF(NINPUT.GE.1) THEN
         CLSPEC = RINPUT(1)
        ELSE
         CALL ASKR('Enter inviscid lift coefficient^',CLSPEC)
        ENDIF
        LALFA = .TRUE.
        ALFA = 0.0
        QINF = 1.0
        CALL SPECCL
        ADEG = ALFA/DTOR
        IF(ABS(ALFA-AWAKE) .GT. 1.0E-5) LWAKE  = .FALSE.
        IF(ABS(ALFA-AVISC) .GT. 1.0E-5) LVCONV = .FALSE.
        IF(ABS(MINF-MVISC) .GT. 1.0E-5) LVCONV = .FALSE.
       ENDIF
C
       IF(LVISC) THEN
         CALL VISCAL(ITMAX)
       ENDIF
ccc    CALL CPX (skipped: triggers X11)
       CALL FCPMIN
C
ccc    IF( LVISC .AND. LPACC .AND. LVCONV ) THEN
       IF( LPACC .AND. (LVCONV .OR. .NOT.LVISC)) THEN
        CALL PLRADD(LUPLR,IPACT)
        CALL PLXADD(LUPLX,IPACT)
       ENDIF
C
       IF(LVISC .AND. .NOT.LPACC .AND. .NOT.LVCONV) THEN
        WRITE(*,*) 'Type "!" to continue iterating'
       ENDIF
C
       COMOLD = COMAND
       ARGOLD = COMARG
C
C--------------------------------------------------------
      ELSEIF(COMAND.EQ.'CL  ' .OR.
     &       COMAND.EQ.'C   '     ) THEN
       IF(.NOT.LRECALC) THEN
        IF(NINPUT.GE.1) THEN
         CLSPEC = RINPUT(1)
        ELSE
         CALL ASKR('Enter lift coefficient^',CLSPEC)
        ENDIF
        LALFA = .FALSE.
        ALFA = 0.0
        QINF = 1.0
        CALL SPECCL
        ADEG = ALFA/DTOR
        IF(ABS(ALFA-AWAKE) .GT. 1.0E-5) LWAKE  = .FALSE.
        IF(ABS(ALFA-AVISC) .GT. 1.0E-5) LVCONV = .FALSE.
        IF(ABS(MINF-MVISC) .GT. 1.0E-5) LVCONV = .FALSE.
       ENDIF
       IF(LVISC) CALL VISCAL(ITMAX)
       CALL FCPMIN
C
ccc    CALL CPX (skipped: triggers X11)
ccc    IF( LVISC .AND. LPACC .AND. LVCONV ) THEN
       IF( LPACC .AND. (LVCONV .OR. .NOT.LVISC)) THEN
        CALL PLRADD(LUPLR,IPACT)
        CALL PLXADD(LUPLX,IPACT)
       ENDIF
C
       IF(LVISC .AND. .NOT.LPACC .AND. .NOT.LVCONV) THEN
        WRITE(*,*) 'Type "!" to continue iterating'
       ENDIF
C
       COMOLD = COMAND
       ARGOLD = COMARG
C
C--------------------------------------------------------
      ELSEIF(COMAND.EQ.'ASEQ' .OR.
     &       COMAND.EQ.'AS  ' .OR.
     &       COMAND.EQ.'CSEQ' .OR.
     &       COMAND.EQ.'CS  '      ) THEN
       LALFA = COMAND.EQ.'ASEQ' .OR.
     &         COMAND.EQ.'AS  '
C
       IF(LALFA) THEN
         IF    (NINPUT.GE.3) THEN
          AA1 = RINPUT(1)
          AA2 = RINPUT(2)
          DAA = RINPUT(3)
         ELSEIF(NINPUT.GE.2) THEN
          AA1 = RINPUT(1)
          AA2 = RINPUT(2)
          DAA = DAA/DTOR
          CALL ASKR('Enter alfa increment   (deg)^',DAA)
         ELSEIF(NINPUT.GE.1) THEN
          AA1 = RINPUT(1)
          AA2 = AA2/DTOR
          CALL ASKR('Enter last  alfa value (deg)^',AA2)
          DAA = DAA/DTOR
          CALL ASKR('Enter alfa increment   (deg)^',DAA)
         ELSE
          AA1 = AA1/DTOR
          CALL ASKR('Enter first alfa value (deg)^',AA1)
          AA2 = AA2/DTOR
          CALL ASKR('Enter last  alfa value (deg)^',AA2)
          DAA = DAA/DTOR
          CALL ASKR('Enter alfa increment   (deg)^',DAA)
         ENDIF
         IF(AA2.LT.AA1) THEN
          DAA = -ABS(DAA)
         ELSE
          DAA =  ABS(DAA)
         ENDIF
         AA1 = AA1*DTOR
         AA2 = AA2*DTOR
         DAA = DAA*DTOR
         NPOINT = 1
         IF(DAA .NE. 0.0) NPOINT = INT((AA2-AA1)/DAA + 0.5) + 1
C
       ELSE
         IF    (NINPUT.GE.3) THEN
          CL1 = RINPUT(1)
          CL2 = RINPUT(2)
          DCL = RINPUT(3)
         ELSEIF(NINPUT.GE.2) THEN
          CL1 = RINPUT(1)
          CL2 = RINPUT(2)
          CALL ASKR('Enter CL increment  ^',DCL)
         ELSEIF(NINPUT.GE.1) THEN
          CL1 = RINPUT(1)
          CALL ASKR('Enter last  CL value^',CL2)
          CALL ASKR('Enter CL increment  ^',DCL)
         ELSE
          CALL ASKR('Enter first CL value^',CL1)
          CALL ASKR('Enter last  CL value^',CL2)
          CALL ASKR('Enter CL increment  ^',DCL)
         ENDIF
         IF(CL2.LT.CL1) THEN
          DCL = -ABS(DCL)
         ELSE
          DCL =  ABS(DCL)
         ENDIF
         NPOINT = 1
         IF(DCL .NE. 0.0) NPOINT = INT((CL2-CL1)/DCL + 0.5) + 1
       ENDIF
C
C- - - - - - - - - - - - - - - - - - 
C
C----- initialize plot
       CALL PLTINI
C
       IF(LPPSHO) THEN
C------ set up for polar plot
C
       ELSE
C------ set up for Cp(x) plot
C
C------ Cp scaling factor
        PFAC = PLOTAR/(CPMAX-CPMIN)
C
C------ determine airfoil box size and location
        CALL AIRLIM(N,X,Y,XMIN,XMAX,YMIN,YMAX)
C
C------ y-offset for airfoil in  Cp vs x  plot
        FACA = FACAIR/(XMAX-XMIN)
        XOFA = XOFAIR*(XMAX-XMIN) - XMIN
        YOFA = YOFAIR*(XMAX-XMIN) - YMAX - CPMAX*PFAC/FACA
C
C------ re-origin for  Cp vs x  plot
        CALL PLOT(0.09 , 0.04 + CPMAX*PFAC + (YMAX-YMIN)*FACA, -3)
C
C------ draw axes and airfoil picture for Cp vs x plot
        CALL CPAXES(LCPGRD,
     &              N,X,Y,XOFA,YOFA,FACA,
     &              CPMIN,CPMAX,CPDEL,PFAC,CH,
     &              'XFOIL',VERSION)
C
C------ set initial x,y-positions of sequence plot label top
        XL = 0.65
        IF(LVISC) XL = 0.48
        YL = -CPMIN*PFAC
C
C------ draw sequence plot label
        CALL SEQLAB(XL,YL,XL1,XL2,XL3,XL4,XL5,XL6,CHSEQ,1,LVISC)
C
        CALL PLFLUSH
C
C------ set label y position
        YL = YL - 0.2*CH
       ENDIF
C
C----- initialize unconverged-point counter
       ISEQEX = 0
       ALAST = ADEG
       CLAST = CL
C
C----- calculate each point, add Cp distribution to plot, and save to polar
       DO 115 IPOINT=1, NPOINT
C
C------- set proper alpha for this point
         IF(LALFA) THEN
          ALFA = AA1 + DAA*FLOAT(IPOINT-1)
         ELSE
          CLSPEC = CL1 + DCL*FLOAT(IPOINT-1)
          CALL SPECCL
         ENDIF
C
         IF(ABS(ALFA-AWAKE) .GT. 1.0E-5) LWAKE  = .FALSE.
         IF(ABS(ALFA-AVISC) .GT. 1.0E-5) LVCONV = .FALSE.
         IF(ABS(MINF-MVISC) .GT. 1.0E-5) LVCONV = .FALSE.
         CALL SPECAL
         ITMAXS = ITMAX + 5
         IF(LVISC) CALL VISCAL(ITMAXS)
C
         ADEG = ALFA/DTOR
C
         CALL FCPMIN
C
C------- add point to buffer polar and/or disk files
ccc      IF( LVISC .AND. LPACC .AND. LVCONV ) THEN
         IF( LPACC .AND. (LVCONV .OR. .NOT.LVISC)) THEN
          CALL PLRADD(LUPLR,IPACT)
          CALL PLXADD(LUPLX,IPACT)
         ENDIF
C
         IF(LPPSHO) THEN
          CALL PLTINI
ccc          CALL PLOTABS(0.5,0.5,-3)
          PSIZE = 1.0*SIZE
          CALL NEWFACTOR(PSIZE)
          CALL PLOT(5.0*CH,7.0*CH,-3)
C
          CH1 = CH*0.90
          CH2 = CH*0.75
          CLEXP = 1.0
C
          DO IP=1, NPOL
           NBLP(IP) = 1
          ENDDO
C
          CALL POLPLT(NAX,NPOL,NAPOL,CPOL,
     &            REYNP1,MACHP1,ACRITP,PTRATP,ETAPP,
     &            NAMEPOL,ICOLP,ILINP,
     &            NFX,NPOLREF,NDREF,CPOLREF,NAMEREF,ICOLR,ISYMR,
     &            ISX,NBLP,CPOLSD ,IMATYP,IRETYP,
     &            ' ','XFOIL',VERSION,
     &            PLOTAR,XCDWID,XALWID,XOCWID,CH1,CH2,CLEXP,
     &            LPGRID,LPCDW,LPLIST,LPLEGN,LAECEN,LPCDH,LPCMDOT,
     &            CPOLPLF,' ',0)
         ELSE
C-------- add alpha, CL, etc. to plot
          CALL SEQPLT(YL,XL1,XL2,XL3,XL4,XL5,XL6,CHSEQ,ADEG,CL,CM,LVISC)
C
C-------- add sonic Cp dashed line if within plot
          IF(CPSTAR.GE.CPMIN) CALL DASH(0.0,XL-CH,-CPSTAR*PFAC)
C
          CALL NEWPEN(2)
          IF(LVISC) THEN
C--------- Plot viscous -Cp distribution on airfoil
           CALL XYLINE(N+NW,X,CPV,-XOFA,FACA,0.0,-PFAC,1)
          ELSE
C--------- Plot inviscid -Cp distribution on airfoil
           CALL XYLINE(N,X,CPI,-XOFA,FACA,0.0,-PFAC,1)
          ENDIF
         ENDIF
C
         CALL PLFLUSH
c###
ccc    call dcpout
C
         IF(LVISC .AND. .NOT.LVCONV) THEN
C-------- increment unconverged-point counter
          ISEQEX = ISEQEX + 1
          IF(ISEQEX .GE. NSEQEX) THEN
           WRITE(*,1150) ISEQEX, ALAST, CLAST
 1150      FORMAT(
     & /' Sequence halted since previous',I3,' points did not converge'
     & /' Last-converged  alpha =', F8.3, '    CL =', F10.5)
           GO TO 116
          ENDIF
         ELSE
C-------- converged OK... reset unconverged-point counter
          ISEQEX = 0
          ALAST = ADEG
          CLAST = CL
         ENDIF
C
  115  CONTINUE
  116  CONTINUE
ccc      CALL ASKC('hit <cr>^',DUMMY,COMARG)
C
       COMOLD = COMAND
       ARGOLD = COMARG
C
C--------------------------------------------------------
      ELSEIF(COMAND.EQ.'SEQP') THEN
       LPPSHO = .NOT.LPPSHO
       IF(LPPSHO) THEN
        WRITE(*,*) 'Polar will be plotted during point sequence'
       ELSE
        WRITE(*,*) 'Cp(x) will be plotted during point sequence'
       ENDIF
C
C--------------------------------------------------------
      ELSEIF(COMAND.EQ.'PACC' .OR.
     &       COMAND.EQ.'P   '      ) THEN
       LPACC = .NOT.LPACC
C
       IF(LPACC) THEN
        IF(NINPUT.GE.1) THEN
C------- slot into which accumulated polar will go
         IP = MIN( MAX( IINPUT(1) , 0 ) , NPOL+1 )
        ELSE
C------- no command argument was given... just use next available slot
         IP = NPOL+1
         PFNAME(IP) = ' '
         PFNAMX(IP) = ' '
        ENDIF
C
        IF(IP.GT.NPOL) THEN
         IF(NPOL.EQ.NPX) THEN
          WRITE(*,*)
          WRITE(*,*) 'Number of polars is at array limit'
          WRITE(*,*) 'New polar will not be stored'
          IPACT = 0
         ELSE
          IPACT = NPOL + 1
          PFNAME(IPACT) = ' '
          PFNAMX(IPACT) = ' '
         ENDIF
C
        ELSE
         IPACT = IP
C
        ENDIF
C
C------ set up for appending to new or existing polar (if IPACT > 0)
        CALL PLRSET(IPACT)
C
C------ jump out if decision was made to abort polar accumulation
        IF(IPACT.LE.0) THEN
         LPACC = .FALSE.
         GO TO 500
        ENDIF
C
        CALL PLRINI(LUPLR,IPACT)
        CALL PLXINI(LUPLX,IPACT)
        WRITE(*,*)
        WRITE(*,*) 'Polar accumulation enabled'
C
       ELSE
        WRITE(*,*)
        WRITE(*,*) 'Polar accumulation disabled'
        IPACT = 0
C
       ENDIF
C
C--------------------------------------------------------
      ELSEIF(COMAND.EQ.'PGET') THEN
        IF(NPOL.GE.NPX) THEN
         WRITE(*,*)
         WRITE(*,*) 'Number of polars is at array limit'
         WRITE(*,*) 'Delete with PDEL if necessary'
         GO TO 500
        ENDIF
C
        IP = NPOL+1
C
        IF(COMARG.EQ.' ') THEN
         CALL ASKS('Enter polar filename^',FNAME)
        ELSE
         FNAME = COMARG
        ENDIF
C
        LU = 17
        CALL POLREAD(LU,FNAME,ERROR,
     &             NAX,NAPOL(IP),CPOL(1,1,IP), 
     &             REYNP1(IP),MACHP1(IP),ACRITP(IP),XSTRIPP(1,IP),
     &             PTRATP(IP),ETAPP(IP),
     &             NAMEPOL(IP),IRETYP(IP),IMATYP(IP),
     &             ISX,NBLP(IP),CPOLSD(1,1,1,IP),
     &             CODEPOL(IP),VERSPOL(IP) )
        IF(ERROR) THEN
         WRITE(*,*) 'Polar file READ error'
        ELSE
         NPOL = IP
         NXYPOL(IP) = 0
         CALL STRIP(NAMEPOL(IP),NNAMEP)
         NEL = 1
         CALL POLWRIT(6,' ',ERROR, .TRUE.,
     &             NAX, 1,NAPOL(IP), CPOL(1,1,IP), IPOL,NIPOL,
     &             REYNP1(IP),MACHP1(IP),ACRITP(IP),XSTRIPP(1,IP),
     &             PTRATP(IP),ETAPP(IP),
     &             NAMEPOL(IP),IRETYP(IP),IMATYP(IP),
     &             ISX,NEL,CPOLSD(1,1,1,IP), JPOL,NJPOL,
     &             CODEPOL(IP),VERSPOL(IP), .FALSE. )
         PFNAME(IP) = FNAME
         WRITE(*,5500) IP
 5500    FORMAT(/' Stored as  Polar', I4)
        ENDIF
C
C--------------------------------------------------------
      ELSEIF(COMAND.EQ.'PWRT') THEN
 75     CONTINUE
        IF(NPOL.EQ.1) THEN
         IP = 1
        ELSEIF(NINPUT.EQ.0) THEN
         CALL PLRSUM(1,NPOL,IPACT)
         CALL ASKI(
     &      'Enter index of polar to write (0=all, -1=abort)^',IP)
         IF(IP.EQ.-1) GO TO 500
        ELSE
         IP = IINPUT(1)
        ENDIF
C
        IF(IP.EQ.0) THEN
         IP1 = 1
         IP2 = NPOL
        ELSEIF(IP.GE.1 .AND. IP.LE.NPOL) THEN
         IP1 = IP
         IP2 = IP
        ELSE
         NINPUT = 0
         GO TO 75
        ENDIF
C
        NEL = 1
        DO IP = IP1, IP2
          LU = 19
          CALL PLRSUM(IP,IP,IPACT)
          CALL STRIP(PFNAME(IP),NPF)
          IF(NPF.EQ.0) THEN
           LINE = 'Enter polar output filename^' 
          ELSE
           LINE = 'Enter polar output filename [' 
     &            // PFNAME(IP)(1:NPF) // ']^'
          ENDIF
          CALL ASKS(LINE,FNAME)
          IF(NPF.NE.0 .AND. FNAME.EQ.' ') FNAME = PFNAME(IP)
C
          NIPOL = NIPOL0
          IF(LCMINP) THEN
           NIPOL = NIPOL + 1
           IPOL(IMC) = NIPOL
          ENDIF
          IF(LHMOMP) THEN
           NIPOL = NIPOL + 1
           IPOL(ICH) = NIPOL
          ENDIF
C
          CALL POLWRIT(LU,FNAME,ERROR, .TRUE.,
     &      NAX, 1,NAPOL(IP),CPOL(1,1,IP), IPOL,NIPOL,
     &      REYNP1(IP),MACHP1(IP),ACRITP(IP),XSTRIPP(1,IP),
     &      PTRATP(IP),ETAPP(IP),
     &      NAMEPOL(IP),IRETYP(IP),IMATYP(IP),
     &      ISX,NEL,CPOLSD(1,1,1,IP), JPOL,NJPOL,
     &      'XFOIL',VERSION, .TRUE. )
          IF(ERROR) THEN
           WRITE(*,1075) IP
 1075      FORMAT(' Polar', I3,'  not written')
          ELSE
           PFNAME(IP) = FNAME
          ENDIF
        ENDDO
C
C--------------------------------------------------------
      ELSEIF(COMAND.EQ.'RGET') THEN
        IF(NPOLREF.GE.NPX) THEN
         WRITE(*,*)
         WRITE(*,*) 'Number of reference polars is at array limit'
         WRITE(*,*) 'Delete with RDEL if necessary'
         GO TO 500
        ENDIF
C
        IR = NPOLREF+1
C
        IF(COMARG.EQ.' ') THEN
         CALL ASKS('Enter reference polar filename^',FNAME)
        ELSE
         FNAME = COMARG
        ENDIF
C
        LU = 9
        OPEN(LU,FILE=FNAME,STATUS='OLD',ERR=27)
        CALL POLREF(LU, FNAME, ERROR,
     &              NFX, NDREF(1,IR), CPOLREF(1,1,1,IR), NAMEREF(IR))
        CLOSE(LU)
        IF(ERROR) GO TO 27
C
        NPOLREF = IR
C
        CALL STRIP(NAMEREF(IR),NNREF)
        IF(NNREF.EQ.0) THEN
          CALL ASKS('Enter label for reference polar^',NAMEREF(IR))
          CALL STRIP(NAMEREF(IR),NNREF)
        ELSE
          WRITE(*,*)
          WRITE(*,*) NAMEREF(IR)
        ENDIF
C
ccc     ICOLR(IR) = NCOLOR - IR + 1
        ICOLR(IR) = 2 + IR
        ISYMR(IR) = MOD(IR,10)
 25   CONTINUE
      GO TO 500
C
 27   CONTINUE
      WRITE(*,*) 'File OPEN error'
C
C--------------------------------------------------------
      ELSEIF(COMAND.EQ.'RDEL') THEN
        IF(NPOLREF.EQ.0) THEN
         WRITE(*,*) 'No reference polars are stored'
         GO TO 500
        ENDIF
C
        IF(NINPUT.GE.1) THEN
         IR = IINPUT(1)
        ELSE
         IR = NPOLREF+1
        ENDIF
C
 35     CONTINUE
C
        IF(IR.EQ.0) THEN
C------- delete all polars
         NPOLREF = 0
C
        ELSEIF(IR.EQ.-1) THEN
C------- abort
         GO TO 500
C
        ELSEIF(IR.LT.-1 .OR. IR.GT.NPOLREF) THEN
         CALL PRFSUM(1,NPOLREF)
         CALL ASKI(
     &      'Specify ref. polar to delete (0 = all, -1 = abort)^',IR)
         GO TO 35
C
        ELSE
C------- delete ref. polar IR
         DO JR = IR+1, NPOLREF
           CALL PRFCOP(JR,JR-1)
           WRITE(*,1310) JR, JR-1
 1410      FORMAT(' Ref.polar',I3,'  moved into ref.polar',I3)
         ENDDO
         NPOLREF = NPOLREF-1
        ENDIF
C
C--------------------------------------------------------
      ELSEIF(COMAND.EQ.'PSUM') THEN
        IF(NPOL.EQ.0) THEN
         WRITE(*,*)
         WRITE(*,*) 'No polars are stored'
         GO TO 500
        ENDIF
C
        CALL PLRSUM(1,NPOL,IPACT)
C
C--------------------------------------------------------
      ELSEIF(COMAND.EQ.'PLIS') THEN
        IF(NPOL.EQ.0) THEN
         WRITE(*,*)
         WRITE(*,*) 'No polars are stored'
         GO TO 500
        ENDIF
C
        IF(NINPUT.EQ.0) THEN
         IP1 = 1
         IP2 = NPOL
        ELSE
         IP = IINPUT(1)
         IF(IP.EQ.0) THEN
          IP1 = 1
          IP2 = NPOL
         ELSEIF(IP.GE.1 .AND. IP.LE.NPOL) THEN
          IP1 = IP
          IP2 = IP
         ELSE
          WRITE(*,*)
          WRITE(*,*) 'Specified stored polar does not exist'
          GO TO 500
         ENDIF
        ENDIF
C
        NIPOL = NIPOL0
        IF(LCMINP) THEN
         NIPOL = NIPOL + 1
         IPOL(IMC) = NIPOL
        ENDIF
        IF(LHMOMP) THEN
         NIPOL = NIPOL + 1
         IPOL(ICH) = NIPOL
        ENDIF
C
        NEL = 1
        DO IP = IP1, IP2
          WRITE(*,3100) IP
 3100     FORMAT(
     &/' =============================================================='
     &/' Polar', I3)
          IA1 = 1
          IA2 = NAPOL(IP)
          CALL POLWRIT(6,' ',ERROR, .TRUE.,
     &                 NAX, IA1,IA2, CPOL(1,1,IP), IPOL,NIPOL,
     &                 REYNP1(IP),MACHP1(IP),ACRITP(IP),XSTRIPP(1,IP),
     &                 PTRATP(IP),ETAPP(IP),
     &                 NAMEPOL(IP), IRETYP(IP),IMATYP(IP),
     &                 ISX,NEL,CPOLSD(1,1,1,IP), JPOL,NJPOL,
     &                 'XFOIL',VERSION, .FALSE.)
        ENDDO
        NIPOL = NIPOL0
C
C--------------------------------------------------------
      ELSEIF(COMAND.EQ.'PDEL') THEN
        IF(NPOL.EQ.0) THEN
         WRITE(*,*) 'No polars are stored'
         GO TO 500
        ENDIF
C
        IF(NINPUT.GE.1) THEN
C------- use command argument
         IP = IINPUT(1)
        ELSE
C------- no argument given... set up for user query test below
         IP = NPOL+1
        ENDIF
C
 40     CONTINUE
        IF(IP.EQ.0) THEN
C------- delete all polars
         NPOL = 0
         IPACT = 0
         LPACC = .FALSE.
C
        ELSEIF(IP.EQ.-1) THEN
C------- abort
         GO TO 500
C
        ELSEIF(IP.LT.-1 .OR. IP.GT.NPOL) THEN
         CALL PLRSUM(1,NPOL,IPACT)
         CALL ASKI(
     &      'Specify polar to delete (0 = all, -1 = abort)^',IP)
         GO TO 40
C
        ELSE
C------- delete polar IP
         IF(IPACT.EQ.IP) THEN
          WRITE(*,*) 'Active polar deleted.  Accumulation turned off'
          IPACT = 0
          LPACC = .FALSE.
         ENDIF
C
         DO JP = IP+1, NPOL
           CALL PLRCOP(JP,JP-1)
           WRITE(*,1310) JP, JP-1
 1310      FORMAT(' Polar',I3,'  moved into polar',I3)
           IF(IPACT.EQ.JP) THEN
            IPACT = JP-1
           ENDIF
         ENDDO
         NPOL = NPOL-1
C
        ENDIF
C
        IF(IPACT.GT.0) THEN
         WRITE(*,1320) IPACT
 1320    FORMAT(' Polar',I3,'  is now active')
        ENDIF
C
C--------------------------------------------------------
      ELSEIF(COMAND.EQ.'PSOR') THEN
        IF(NPOL.EQ.0) THEN
         WRITE(*,*) 'No polars are stored'
         GO TO 500
        ENDIF
C
        IF(NINPUT.GE.1) THEN
C------- use command argument
         IP = IINPUT(1)
        ELSE
C------- no argument given... set up for user query test below
         IP = NPOL+1
        ENDIF
C
C------ sort polars in increasing alpha
        IDSORT = IAL
C
 42     CONTINUE
        IF    (IP.EQ.-1) THEN
C------- abort
         GO TO 500
C
        ELSEIF(IP.LT.-1 .OR. IP.GT.NPOL) THEN
         CALL PLRSUM(1,NPOL,IPACT)
         CALL ASKI(
     &      'Specify polar to sort (0 = all, -1 = abort)^',IP)
         GO TO 42
C
        ELSE
C------- sort polar(s)
         IF(IP.EQ.0) THEN
          IP1 = 1
          IP2 = NPOL
         ELSE
          IP1 = IP
          IP2 = IP
         ENDIF
         DO JP = IP1, IP2
           CALL PLRSRT(JP,IDSORT)
         ENDDO
        ENDIF
C
C--------------------------------------------------------
      ELSEIF(COMAND.EQ.'PPLO' .OR.
     &       COMAND.EQ.'PP  '      ) THEN
C------ set temporary polar-size array to plot only selected polars
        IF(NINPUT.EQ.0) THEN
C------- no polars specified... plot all of them
         DO IP=1, NPOL
           NAPOLT(IP) = NAPOL(IP)
         ENDDO
        ELSE
C------- set up to plot only specified polars
         DO IP=1, NPOL
           NAPOLT(IP) = 0
         ENDDO
         DO K=1, NINPUT
           IP = IINPUT(K)
           IF(IP.GE.1 .AND. IP.LE.NPOL) NAPOLT(IP) = NAPOL(IP)
         ENDDO
        ENDIF
C
        CALL PLTINI
ccc        CALL PLOTABS(0.5,0.5,-3)
        PSIZE = 1.0*SIZE
        CALL NEWFACTOR(PSIZE)
        CALL PLOT(5.0*CH,7.0*CH,-3)
C
        CH1 = CH*0.90
        CH2 = CH*0.75
        CLEXP = 1.0
        DO IP=1, NPOL
          NBLP(IP) = 1
        ENDDO
C
        CALL POLPLT(NAX,NPOL,NAPOLT,CPOL,
     &          REYNP1,MACHP1,ACRITP,PTRATP,ETAPP,
     &          NAMEPOL,ICOLP,ILINP,
     &          NFX,NPOLREF,NDREF,CPOLREF,NAMEREF,ICOLR,ISYMR,
     &          ISX,NBLP,CPOLSD ,IMATYP,IRETYP,
     &          ' ','XFOIL',VERSION,
     &          PLOTAR,XCDWID,XALWID,XOCWID,CH1,CH2,CLEXP,
     &          LPGRID,LPCDW,LPLIST,LPLEGN,LAECEN,LPCDH,LPCMDOT,
     &          CPOLPLF,' ',0)
C
C--------------------------------------------------------
      ELSEIF(COMAND.EQ.'APLO' .OR.
     &       COMAND.EQ.'AP  '      ) THEN
        IF(NPOL.EQ.0) THEN
         WRITE(*,*)
         WRITE(*,*) 'No polars are stored'
         GO TO 500
        ENDIF
C
        IF(NINPUT.EQ.0) THEN
         NPPAI = NPOL
         DO K=1, NPPAI
           IPPAI(K) = K
         ENDDO
        ELSE
         NPPAI = MIN( NINPUT , NPX )
         DO K=1, NPPAI
           IINP = IINPUT(K)
           IF(IINP.GE.1 .AND. IINP.LE.NPOL) THEN
            IPPAI(K) = IINP
           ELSE
            IPPAI(K) = 0
           ENDIF
         ENDDO
        ENDIF
C
        CALL PPAPLT(NPPAI,IPPAI)
C
C--------------------------------------------------------
      ELSEIF(COMAND.EQ.'ASET') THEN
       IF(NPOL.EQ.0) THEN
        WRITE(*,*)
        WRITE(*,*) 'No polar airfoils are stored'
        GO TO 500
       ENDIF
C
 50    CONTINUE
       IF(NINPUT.EQ.0) THEN
        IF(NPOL.EQ.1) THEN
         IP = 1
        ELSE
         CALL PLRSUM(1,NPOL,IPACT)
         CALL ASKI('Enter index of polar airfoil to set^',IP)
        ENDIF
       ELSE
        IP = IINPUT(1)
       ENDIF
C
       IF(IP.EQ.0) THEN
        GO TO 500
       ELSEIF(IP.LT.1 .OR. IP.GT.NPOL) THEN
        WRITE(*,*)
        WRITE(*,*) 'Specified polar airfoil does not exist'
        NINPUT = 0
        GO TO 50
       ENDIF
C
       WRITE(*,*)
       WRITE(*,*) 'Current airfoil will be overwritten.  Proceed?  Y'
       READ(*,1000) ANS
 1000  FORMAT(A)
C
       IF(INDEX('Nn',ANS) .NE. 0) THEN
        WRITE(*,*) 'No action taken'
        GO TO 500
       ELSE
        CALL APCOPY(IP)
       ENDIF
C
C--------------------------------------------------------
      ELSEIF(COMAND.EQ.'PREM') THEN
       IF(NPOL.EQ.0) THEN
        WRITE(*,*)
        WRITE(*,*) 'No polars are stored'
        GO TO 500
       ENDIF
C
 52    CONTINUE
       IF(NINPUT.EQ.0) THEN
        IF(NPOL.EQ.1) THEN
         IP = 1
        ELSE
         CALL PLRSUM(1,NPOL,IPACT)
         CALL ASKI('Enter index of polar to modify^',IP)
        ENDIF
       ELSE
        IP = IINPUT(1)
       ENDIF
C
       IF(IP.EQ.0) THEN
        GO TO 500
       ELSEIF(IP.LT.1 .OR. IP.GT.NPOL) THEN
        WRITE(*,*)
        WRITE(*,*) 'Specified polar airfoil does not exist'
        NINPUT = 0
        GO TO 52
       ENDIF
C
       IF(NINPUT.GE.2) THEN
        NREM = NINPUT - 1
       ELSE
        NIPOL = NIPOL0
        IF(LCMINP) THEN
         NIPOL = NIPOL + 1
         IPOL(IMC) = NIPOL
        ENDIF
        IF(LHMOMP) THEN
         NIPOL = NIPOL + 1
         IPOL(ICH) = NIPOL
        ENDIF
C
        WRITE(*,3100) IP
        IA1 = 1
        IA2 = NAPOL(IP)
        CALL POLWRIT(6,' ',ERROR, .TRUE.,
     &                 NAX, IA1,IA2, CPOL(1,1,IP), IPOL,NIPOL,
     &                 REYNP1(IP),MACHP1(IP),ACRITP(IP),XSTRIPP(1,IP),
     &                 PTRATP(IP),ETAPP(IP),
     &                 NAMEPOL(IP), IRETYP(IP),IMATYP(IP),
     &                 ISX,1,CPOLSD(1,1,1,IP), JPOL,NJPOL,
     &                 'XFOIL',VERSION, .FALSE. )
 53     WRITE(*,3220)
 3220   FORMAT(/' Enter alpha(s) of points to be removed:  ', $)
        READ(*,1000) LINE
        NREM = 19
        CALL GETFLT(LINE,RINPUT(2),NREM,ERROR)
        IF(ERROR) GO TO 53
       ENDIF
C
C----- go over specified alphas to be removed
       DO 55 IREM = 1, NREM
C------- check all alpha points in polar IP
         DO IA = 1, NAPOL(IP)
           ADIF = CPOL(IA,IAL,IP) - RINPUT(IREM+1)
           IF(ABS(ADIF) .LT. 0.0005) THEN
C---------- alphas match within 3-digit print tolerance... 
C-             remove point by pulling down all points above it
            DO JA = IA, NAPOL(IP)-1
              DO K = 1, IPTOT
                CPOL(JA,K,IP) = CPOL(JA+1,K,IP)
              ENDDO
              DO K = 1, JPTOT
                CPOLSD(JA,1,K,IP) = CPOLSD(JA+1,1,K,IP)
                CPOLSD(JA,2,K,IP) = CPOLSD(JA+1,2,K,IP)
              ENDDO
            ENDDO
C---------- shrink polar by 1
            NAPOL(IP) = NAPOL(IP) - 1
C
            IF(NAPOL(IP).LE.0) THEN
C----------- last point has been removed... eliminate this polar IP
             DO JP = IP+1, NPOL
               CALL PLRCOP(JP,JP-1)
               IF(IPACT.EQ.JP) IPACT = JP-1
               WRITE(*,1310) JP, JP-1
             ENDDO
             NPOL = NPOL-1
C
             IF(IPACT.GT.0) THEN
              WRITE(*,1320) IPACT
             ENDIF
C
             GO TO 500
            ENDIF
C
C---------- go to next specified alpha to be removed
            GO TO 55
           ENDIF
         ENDDO
 55    CONTINUE
C         
C--------------------------------------------------------
      ELSEIF(COMAND.EQ.'PNAM') THEN
       IF(NPOL.EQ.0) THEN
        WRITE(*,*)
        WRITE(*,*) 'No polars are stored'
        GO TO 500
       ENDIF
C
 58    CONTINUE
       IF(NINPUT.EQ.0) THEN
        IF(NPOL.EQ.1) THEN
         IP = 1
        ELSE
         CALL PLRSUM(1,NPOL,IPACT)
         CALL ASKI('Enter index of polar to modify^',IP)
        ENDIF
       ELSE
        IP = IINPUT(1)
       ENDIF
C
       IF(IP.EQ.0) THEN
        GO TO 500
       ELSEIF(IP.LT.1 .OR. IP.GT.NPOL) THEN
        WRITE(*,*)
        WRITE(*,*) 'Specified polar airfoil does not exist'
        NINPUT = 0
        GO TO 58
       ENDIF
C
       NIPOL = NIPOL0
       IF(LCMINP) THEN
        NIPOL = NIPOL + 1
        IPOL(IMC) = NIPOL
       ENDIF
       IF(LHMOMP) THEN
        NIPOL = NIPOL + 1
        IPOL(ICH) = NIPOL
       ENDIF
C
       WRITE(*,3100) IP
       IA1 = 0
       IA2 = -1
       CALL POLWRIT(6,' ',ERROR, .TRUE.,
     &                NAX, IA1,IA2, CPOL(1,1,IP), IPOL,NIPOL,
     &                REYNP1(IP),MACHP1(IP),ACRITP(IP),XSTRIPP(1,IP),
     &                PTRATP(IP),ETAPP(IP),
     &                NAMEPOL(IP), IRETYP(IP),IMATYP(IP),
     &                ISX,1,CPOLSD(1,1,1,IP), JPOL,NJPOL,
     &                'XFOIL',VERSION, .FALSE. )
       NIPOL = NIPOL0
       WRITE(*,3320)
 3320  FORMAT(/' Enter new airfoil name of polar:  ', $)
       READ(*,1000) NAMEPOL(IP)
       CALL STRIP(NAMEPOL(IP),NNP)
C
C--------------------------------------------------------
      ELSEIF(COMAND.EQ.'PPAX') THEN
        CALL POLAXI(CPOLPLF,XCDWID,XALWI,XOCWID)
C
C--------------------------------------------------------
      ELSEIF(COMAND.EQ.'CREF') THEN
       LCPREF = .NOT. LCPREF
       IF(LCPREF) THEN
        WRITE(*,*) 'Reference Cp plotting enabled'
       ELSE
        WRITE(*,*) 'Reference Cp plotting disabled'
       ENDIF
C
C--------------------------------------------------------
      ELSEIF(COMAND.EQ.'FREF') THEN
       LFOREF = .NOT. LFOREF
       IF(LFOREF) THEN
        WRITE(*,*) 'Reference force plotting enabled'
       ELSE
        WRITE(*,*) 'Reference force plotting disabled'
       ENDIF
C
C--------------------------------------------------------
      ELSEIF(COMAND.EQ.'CPX ' .OR.
     &       COMAND.EQ.'CP  '      ) THEN
ccc    CALL CPX (skipped: triggers X11)
C
C--------------------------------------------------------
      ELSEIF(COMAND.EQ.'UEX ' .OR.
     &       COMAND.EQ.'UE  '      ) THEN
       CALL UEX
C
C--------------------------------------------------------
      ELSEIF(COMAND.EQ.'GRID') THEN
       LCPGRD = .NOT.LCPGRD
       IF(LCPGRD) THEN
        WRITE(*,*) 'Cp grid overlay enabled'
       ELSE
        WRITE(*,*) 'Cp grid overlay disabled'
       ENDIF
C
C--------------------------------------------------------
      ELSEIF(COMAND.EQ.'CPV ') THEN
       CALL CPVEC
C
C--------------------------------------------------------
      ELSEIF(COMAND.EQ.'BL  ') THEN
       IF(.NOT.LVCONV) THEN
        WRITE(*,*) 'Compute valid viscous solution first'
        GO TO 500
       ENDIF
C
       IF(NINPUT.GE.1) THEN
        NPR = MIN( IINPUT(1) , NPRX )
       ELSE
        NPR = 21
        WRITE(*,*) 'Using default number of profiles:', NPR
       ENDIF
C
       IF(NPR.GT.1) THEN
C------ set NPR points along surface, offset slightly for the locating logic
        DOFF = 0.00001*(S(N)-S(1))
        DO IPR = 1, NPR
          FRAC = FLOAT(IPR-1)/FLOAT(NPR-1)
          SPR = S(1) + (S(N)-S(1))*FRAC
          XPR(IPR) = SEVAL(SPR,X,XP,S,N) + DOFF*DEVAL(SPR,Y,YP,S,N)
          YPR(IPR) = SEVAL(SPR,Y,YP,S,N) - DOFF*DEVAL(SPR,X,XP,S,N)
        ENDDO
       ENDIF
C
ccc    CALL CPX (skipped: triggers X11)
       CALL DPLOT(NPR,XPR,YPR)
C
C--------------------------------------------------------
      ELSEIF(COMAND.EQ.'BLC ') THEN
       IF(.NOT.LVCONV) THEN
        WRITE(*,*) 'Compute valid viscous solution first'
        GO TO 500
       ENDIF
C
       NPR = 0
ccc    CALL CPX (skipped: triggers X11)
       CALL DPLOT(NPR,XPR,YPR)
C
C--------------------------------------------------------
      ELSEIF(COMAND.EQ.'BLF ') THEN
C
       NPR = 2
       DO IPR = 1, NPR
C
c       WRITE(*,'(1X,A,$)') 'Enter  x/c_BL, delta : '
c       READ(*,1000) LINE
c       NINP = 2
c       CALL GETFLT(LINE,RINPUT,NINP,ERROR)
c       IF(ERROR .OR. NINP.EQ.0) THEN
c        GO TO 500
c       ELSE
c        SGN = SIGN(1.0,RINPUT(1))
c        XOC = ABS(RINPUT(1))
c        DPR = RINPUT(2)
c       ENDIF

        if    (ipr.eq.1) then
         xoc = 0.4
         sgn = 1.0
        elseif(ipr.eq.2) then
         xoc = 0.4
         sgn = -1.0
        endif

        if(ninput .gt. 0) then
         dpr = rinput(1)
        else
         dpr = 0.01
        endif

C       
       IF(SGN .GT. 0.0) THEN
        SPR = SLE + (S(1)-SLE)*XOC
       ELSE
        SPR = SLE + (S(N)-SLE)*XOC
       ENDIF
C
       XPRI = XLE + (XTE-XLE)*XOC
       CALL SINVRT(SPR,XPRI,X,XP,S,N)
C
       DOFF = 0.00001*(S(N)-S(1))
       XPR(IPR) = SEVAL(SPR,X,XP,S,N) + DOFF*DEVAL(SPR,Y,YP,S,N)
       YPR(IPR) = SEVAL(SPR,Y,YP,S,N) - DOFF*DEVAL(SPR,X,XP,S,N)
C
       CALL FBLGET(XPR(IPR),YPR(IPR), DPR,FPR(IPR) )
C
       enddo

       WRITE(*,*)
       DO IPR = 1, NPR
         WRITE(*,7720) 'xBL, Fint =', XPR(IPR), FPR(IPR)*1.0E4
       ENDDO
 7720  FORMAT(1X,A,F7.3,F12.6)

ccc       GO TO 770
C
C--------------------------------------------------------
      ELSEIF(COMAND.EQ.'BLWT') THEN
       IF(NINPUT.GE.1) THEN
        UPRWT = RINPUT(1)
       ELSE
        WRITE(*,*) 'Current u/Qinf profile plot weight =', UPRWT
        CALL ASKR('Enter new plot weight^',UPRWT)
       ENDIF
C
ccc    CALL CPX (skipped: triggers X11)
C
C--------------------------------------------------------
      ELSEIF(COMAND.EQ.'FMOM') THEN
       CALL MHINGE
       WRITE(*,1500) XOF,YOF,HMOM,HFX,HFY
 1500  FORMAT(/' Flap hinge x,y :', 2F8.4/
     &         '                                           2  2'/
     &         ' Hinge moment/span = ',F8.6,'  x  1/2 rho V  c '/
     &         '                                           2   '/
     &         ' x-Force     /span = ',F8.6,'  x  1/2 rho V  c '/
     &         '                                           2   '/
     &         ' y-Force     /span = ',F8.6,'  x  1/2 rho V  c '/)
C
C--------------------------------------------------------
      ELSEIF(COMAND.EQ.'FNEW') THEN
       IF    (NINPUT.GE.2) THEN
        XOF = RINPUT(1)
        YOF = RINPUT(2)
       ELSEIF(NINPUT.GE.1) THEN
        XOF = RINPUT(1)
        YOF = -999.0
       ELSE
        XOF = -999.0
        YOF = -999.0
       ENDIF
       LFLAP = .FALSE.
C
C--------------------------------------------------------
      ELSEIF(COMAND.EQ.'VELS') THEN
       IF    (NINPUT.GE.2) THEN
        XXX = RINPUT(1)
        YYY = RINPUT(2)
       ELSEIF(NINPUT.GE.1) THEN
        XXX = RINPUT(1)
        CALL ASKR('Enter y^',YYY)
       ELSE
        CALL ASKR('Enter x^',XXX)
        CALL ASKR('Enter y^',YYY)
       ENDIF
       CALL PSILIN(0,XXX,YYY,-1.0,0.0,PSI,VVV,.FALSE.,.TRUE.)
       CALL PSILIN(0,XXX,YYY, 0.0,1.0,PSI,UUU,.FALSE.,.TRUE.)
       QQQ = SQRT(UUU**2 + VVV**2)
       CPP = 1.0 - (UUU**2 + VVV**2)
       WRITE(*,1800) UUU,VVV,QQQ,CPP
 1800  FORMAT(/' u/Uinf = ', F8.4, '   v/Uinf = ', F8.4
     &        /' q/Uinf = ', F8.4, '   Cp     = ', F8.4 /  )
C
       COMOLD = COMAND
       ARGOLD = COMARG
C
C--------------------------------------------------------
      ELSEIF(COMAND.EQ.'DUMP') THEN
       CALL BLDUMP(COMARG)
C
C--------------------------------------------------------
      ELSEIF(COMAND.EQ.'CPWR') THEN
       CALL CPDUMP(COMARG)
C
C--------------------------------------------------------
      ELSEIF(COMAND.EQ.'CPMN') THEN
       IF(LVISC)THEN
        WRITE(*,1769) CPMNI, XCPMNI, CPMNV, XCPMNV
 1769   FORMAT('  Minimum Inviscid Cp =',F8.4,'   at x =',F8.4
     &       / '  Minimum Viscous  Cp =',F8.4,'   at x =',F8.4 )
       ELSE
        WRITE(*,1779) CPMNI, XCPMNI
 1779   FORMAT('  Minimum Inviscid Cp =',F8.4,'   at x =',F8.4)
       ENDIF
C
C--------------------------------------------------------
      ELSEIF(COMAND.EQ.'CINC') THEN
       LCMINP = .NOT.LCMINP
       IF(LCMINP) THEN
        WRITE(*,*) 'Min Cp will be written to polar save file'
       ELSE 
        WRITE(*,*) 'Min Cp won''t be written to polar save file'
       ENDIF
C
C--------------------------------------------------------
      ELSEIF(COMAND.EQ.'HINC') THEN
       LHMOMP = .NOT.LHMOMP
       IF(LHMOMP) THEN
        WRITE(*,*) 'Hinge moment will be written to polar save file'
        IF(.NOT.LFLAP) THEN
         WRITE(*,*)
         WRITE(*,*) 'Note: Flap hinge location not defined'
         WRITE(*,*) '      Set it with FNEW command'
        ENDIF
       ELSE 
        WRITE(*,*) 'Hinge moment won''t be written to polar save file'
       ENDIF
C
C--------------------------------------------------------
      ELSEIF(COMAND.EQ.'ANNO') THEN
       IF(LPLOT) THEN
        CALL ANNOT(CH)
       ELSE
        WRITE(*,*) 'No active plot to annotate'
       ENDIF
C
C--------------------------------------------------------
      ELSEIF(COMAND.EQ.'VPLO' .OR.
     &       COMAND.EQ.'VP  '      ) THEN
       CALL BLPLOT
C
C--------------------------------------------------------
      ELSEIF(COMAND.EQ.'NAME') THEN
       IF(COMARG.EQ.' ') THEN
        CALL NAMMOD(NAME,0,-1)
       ELSE
        NAME = COMARG
       ENDIF
       CALL STRIP(NAME,NNAME)
C
C--------------------------------------------------------
      ELSEIF(COMAND.EQ.'NINC') THEN
       CALL NAMMOD(NAME,1,1)
       CALL STRIP(NAME,NNAME)
C
C--------------------------------------------------------
      ELSEIF(COMAND.EQ.'NDEC') THEN
       CALL NAMMOD(NAME,-1,1)
       CALL STRIP(NAME,NNAME)
C
C--------------------------------------------------------
      ELSEIF(COMAND.EQ.'DAMP') THEN
       IF(IDAMP.EQ.0) THEN
        IDAMP = 1
        WRITE(*,*) 'Modified amplification used'
       ELSE 
        IDAMP = 0
        WRITE(*,*) 'Original amplification used'
       ENDIF
C--------------------------------------------------------
      ELSE
       WRITE(*,8000) COMAND
 8000  FORMAT(1X,A4,' command not recognized.  Type a "?" for list')

      ENDIF
C
C---- go back to top of menu loop
      GO TO 500
C
C--------------------------------------------
 2100 FORMAT(/' * Polar is being accumulated.'
     &       /' * Cannot change its parameters in midstream.')
      END ! OPER


      SUBROUTINE FCPMIN
C------------------------------------------------
C     Finds minimum Cp on dist for cavitation work
C------------------------------------------------
      INCLUDE 'XFOIL.INC'
C
      XCPMNI = X(1)
      XCPMNV = X(1)
      CPMNI = CPI(1)
      CPMNV = CPV(1)
C
      DO I = 2, N + NW
        IF(CPI(I) .LT. CPMNI) THEN
         XCPMNI = X(I)
         CPMNI = CPI(I)
        ENDIF
        IF(CPV(I) .LT. CPMNV) THEN
         XCPMNV = X(I)
         CPMNV = CPV(I)
        ENDIF
      ENDDO
C

      IF(LVISC)THEN
        CPMN = CPMNV
      ELSE
        CPMN = CPMNI
C
        CPMNV = CPMNI
        XCPMNV = XCPMNI
      ENDIF
C
      RETURN
      END ! FCPMIN



      SUBROUTINE MRSHOW(LM,LR)
      INCLUDE 'XFOIL.INC'
      LOGICAL LM, LR
C
      IF(LM .OR. LR) WRITE(*,*)
C
      IF(LM) THEN
       IF(MATYP.EQ.1) WRITE(*,1100) MINF1
       IF(MATYP.EQ.2) WRITE(*,1100) MINF1, ' / sqrt(CL)'
       IF(MATYP.EQ.3) WRITE(*,1100) MINF1, ' / CL'
      ENDIF
C
      IF(LR) THEN
       IF(RETYP.EQ.1) WRITE(*,1200) REINF1
       IF(RETYP.EQ.2) WRITE(*,1200) REINF1, ' / sqrt(CL)'
       IF(RETYP.EQ.3) WRITE(*,1200) REINF1, ' / CL'
      ENDIF
C
      RETURN
C
 1100 FORMAT(1X,'M  =' , F10.4, A)
 1200 FORMAT(1X,'Re =' , G12.4, A)
      END ! MRSHOW



      SUBROUTINE NAMMOD(NAME,KDEL,KMOD0)
      CHARACTER*(*) NAME
C-------------------------------------------
C     Requests new modified NAME with 
C     version number in brackets, e.g.
C            NACA 0012  [5]
C
C     If bracketed index exists in NAME,
C        it is incremented by KDEL.
C     If no bracketed index exists, it 
C        is added with initial value KMOD0,
C        unless KMOD0 is negative in which 
C        case nothing is added.
C-------------------------------------------
      CHARACTER*48 NAMDEF
C
      CALL STRIP(NAME,NNAME)
      KBRACK1 = INDEX(NAME,'[')
      KBRACK2 = INDEX(NAME,']')
C
      NAMDEF = NAME(1:NNAME)
C
      IF(KBRACK1.NE.0 .AND. 
     &   KBRACK2.NE.0 .AND. KBRACK2-KBRACK1.GT.1) THEN
C----- brackets exist... get number, (go get user's input on READ error)
       READ(NAME(KBRACK1+1:KBRACK2-1),*,ERR=40) KMOD
       KMOD = IABS(KMOD)
       KMODP = MOD( KMOD+KDEL , 100 )
       IF(KBRACK1.GE.2) THEN
        NAME = NAME(1:KBRACK1-1)
       ELSE
        NAME = ' '
       ENDIF
       CALL STRIP(NAME,NNAME)
      ELSEIF(KMOD0.GT.0) THEN
       KMODP = MOD( KMOD0 , 100 )
      ELSE
       KMODP = 0
      ENDIF
C
      IF    (KMODP.GE.10) THEN
       NAMDEF = NAME(1:NNAME) // ' [  ]'
       WRITE(NAMDEF(NNAME+3:NNAME+4),1020) KMODP
 1020  FORMAT(I2)
      ELSEIF(KMODP.GE. 1) THEN
       NAMDEF = NAME(1:NNAME) // ' [ ]'
       WRITE(NAMDEF(NNAME+3:NNAME+3),1025) KMODP
 1025  FORMAT(I1)
      ENDIF
C
 40   WRITE(*,1040) NAMDEF
 1040 FORMAT(/' Enter airfoil name or <return> for default:  ',A)
      READ(*,1000) NAME
 1000 FORMAT(A)
      IF(NAME .EQ. ' ') NAME = NAMDEF
C
      RETURN
      END ! NAMMOD



      SUBROUTINE BLDUMP(FNAME1)
      INCLUDE 'XFOIL.INC'
      CHARACTER*(*) FNAME1
C
      CHARACTER*80 FILDEF
C
      CHARACTER*1 DELIM
      CHARACTER*256 LINE
C
      IF    (KDELIM.EQ.0) THEN
       DELIM = ' '
      ELSEIF(KDELIM.EQ.1) THEN
       DELIM = ','
      ELSEIF(KDELIM.EQ.2) THEN
       DELIM = CHAR(9)
      ELSE
       WRITE(*,*) '? Illegal delimiter.  Using blank.'
       DELIM = ' '
      ENDIF
C
 1000 FORMAT(50A)
C
      IF(FNAME1(1:1).NE.' ') THEN
       FNAME = FNAME1
      ELSE
C----- no argument... get it somehow
       IF(NPREFIX.GT.0) THEN
C------ offer default using existing prefix
        FILDEF = PREFIX(1:NPREFIX) // '.bl'
        WRITE(*,1100) FILDEF
 1100   FORMAT(/' Enter filename:  ', A)
        READ(*,1000) FNAME
        CALL STRIP(FNAME,NFN)
        IF(NFN.EQ.0) FNAME = FILDEF
       ELSE
C------ nothing available... just ask for filename
        CALL ASKS('Enter filename^',FNAME)
       ENDIF
      ENDIF
C
      LU = 19
      OPEN(LU,FILE=FNAME,STATUS='UNKNOWN')
      REWIND(LU)
C
      IF(KDELIM.EQ.0) THEN
       WRITE(LU,1000)
     & '#    s        x        y     Ue/Vinf    Dstar     Theta ',
     & '     Cf       H'
C         1.23456  0.23451  0.23451  0.23451  0.012345  0.001234  0.004123  10.512
      ELSE
       WRITE(LU,1000)
     & '#s'     ,DELIM,
     & 'x'      ,DELIM,
     & 'y'      ,DELIM,
     & 'Ue/Vinf',DELIM,
     & 'Dstar'  ,DELIM,
     & 'Theta'  ,DELIM,
     & 'Cf'     ,DELIM,
     & 'H'
      ENDIF
C
      CALL COMSET
      HSTINV = GAMM1*(MINF/QINF)**2 / (1.0 + 0.5*GAMM1*MINF**2)
C
      DO 10 I=1, N
        IS = 1
        IF(GAM(I) .LT. 0.0) IS = 2
C
        IF(LIPAN .AND. LVISC) THEN
          IF(IS.EQ.1) THEN
            IBL = IBLTE(IS) - I + 1
          ELSE
            IBL = IBLTE(IS) + I - N
          ENDIF
          DS = DSTR(IBL,IS)
          TH = THET(IBL,IS)
          CF =  TAU(IBL,IS)/(0.5*QINF**2)
          IF(TH.EQ.0.0) THEN
           H = 1.0
          ELSE
           H = DS/TH
          ENDIF
        ELSE
          DS = 0.
          TH = 0.
          CF = 0.
          H = 1.0
        ENDIF
        UE = (GAM(I)/QINF)*(1.0-TKLAM) / (1.0 - TKLAM*(GAM(I)/QINF)**2)
        AMSQ = UE*UE*HSTINV / (GAMM1*(1.0 - 0.5*UE*UE*HSTINV))
        CALL HKIN( H, AMSQ, HK, DUMMY, DUMMY)
C
        IF(KDELIM.EQ.0) THEN
         WRITE(LU,8500) S(I), X(I), Y(I), UE, DS, TH, CF, HK
 8500    FORMAT(1X, 4F9.5, 3F10.6, F10.3)
C
        ELSE
         WRITE(LINE,8510) 
     &     S(I),DELIM,
     &     X(I),DELIM,
     &     Y(I),DELIM,
     &     UE  ,DELIM,
     &     DS  ,DELIM,
     &     TH  ,DELIM,
     &     CF  ,DELIM,
     &     HK
 8510    FORMAT(1X, 4(F9.5,A), 3(F10.6,A), F10.3)
         CALL BSTRIP(LINE,NLINE)
         WRITE(LU,1000) LINE(1:NLINE)
        ENDIF
C
  10  CONTINUE
C
      IF(LWAKE) THEN
        IS = 2
        DO 20 I=N+1, N+NW
          IBL = IBLTE(IS) + I - N
          DS = DSTR(IBL,IS)
          TH = THET(IBL,IS)
          H = DS/TH
          CF = 0.
          UI = UEDG(IBL,IS)
          UE = (UI/QINF)*(1.0-TKLAM) / (1.0 - TKLAM*(UI/QINF)**2)
          AMSQ = UE*UE*HSTINV / (GAMM1*(1.0 - 0.5*UE*UE*HSTINV))
          CALL HKIN( H, AMSQ, HK, DUMMY, DUMMY)
C
          IF(KDELIM.EQ.0) THEN
           WRITE(LU,8500) S(I), X(I), Y(I), UE, DS, TH, CF, HK
C
          ELSE
           WRITE(LINE,8510) 
     &      S(I),DELIM,
     &      X(I),DELIM,
     &      Y(I),DELIM,
     &      UE  ,DELIM,
     &      DS  ,DELIM,
     &      TH  ,DELIM,
     &      CF  ,DELIM,
     &      HK
           CALL BSTRIP(LINE,NLINE)
           WRITE(LU,1000) LINE(1:NLINE)
          ENDIF
 20     CONTINUE
      ENDIF
C
      CLOSE(LU)
      RETURN
      END ! BLDUMP



      SUBROUTINE CPDUMP(FNAME1)
      INCLUDE 'XFOIL.INC'
      CHARACTER*(*) FNAME1
C
      CHARACTER*80 FILDEF
C
      CHARACTER*1 DELIM
      CHARACTER*128 LINE
C
      IF    (KDELIM.EQ.0) THEN
       DELIM = ' '
      ELSEIF(KDELIM.EQ.1) THEN
       DELIM = ','
      ELSEIF(KDELIM.EQ.2) THEN
       DELIM = CHAR(9)
      ELSE
       WRITE(*,*) '? Illegal delimiter.  Using blank.'
       DELIM = ' '
      ENDIF
C
 1000 FORMAT(8A)
C
      IF(FNAME1(1:1).NE.' ') THEN
       FNAME = FNAME1
      ELSE
C----- no argument... get it somehow
       IF(NPREFIX.GT.0) THEN
C------ offer default using existing prefix
        FILDEF = PREFIX(1:NPREFIX) // '.cp'
        WRITE(*,1100) FILDEF
 1100   FORMAT(/' Enter filename:  ', A)
        READ(*,1000) FNAME
        CALL STRIP(FNAME,NFN)
        IF(NFN.EQ.0) FNAME = FILDEF
       ELSE
C------ nothing available... just ask for filename
        CALL ASKS('Enter filename^',FNAME)
       ENDIF
      ENDIF
C
C
      LU = 19
      OPEN(LU,FILE=FNAME,STATUS='UNKNOWN')
      REWIND(LU)
C
      IF(KDELIM.EQ.0) THEN
       WRITE(LU,1000)
     &  '#      x          Cp  '
C            0.23451    0.23451
      ELSE
       WRITE(LU,1000)
     &  '#x', DELIM,
     &  'Cp'
C
      ENDIF
C
      CALL COMSET
C
      BETA = SQRT(1.0 - MINF**2)
      BFAC = 0.5*MINF**2 / (1.0 + BETA)
C
      DO 10 I=1, N
        CPINC = 1.0 - (GAM(I)/QINF)**2
        DEN = BETA + BFAC*CPINC
        CPCOM = CPINC / DEN
C
        IF(KDELIM.EQ.0) THEN
         WRITE(LU,8500) X(I), CPCOM
 8500    FORMAT(1X,2F11.5)
        ELSE
         WRITE(LINE,8510) 
     &    X(I) , DELIM,
     &    CPCOM
 8510    FORMAT(1X,2(F11.5,A))
         CALL BSTRIP(LINE,NLINE)
         WRITE(LU,1000) LINE(1:NLINE)
        ENDIF
  10  CONTINUE
C
      CLOSE(LU)
      RETURN
      END ! CPDUMP



      SUBROUTINE MHINGE
C----------------------------------------------------
C     Calculates the hinge moment of the flap about
C     (XOF,YOF) by integrating surface pressures.
C----------------------------------------------------
      INCLUDE 'XFOIL.INC'
C
      IF(.NOT.LFLAP) THEN
C
        CALL GETXYF(X,XP,Y,YP,S,N, TOPS,BOTS,XOF,YOF)
        LFLAP = .TRUE.
C
      ELSE
C
C------ find top and bottom y at hinge x location
        TOPS = XOF
        BOTS = S(N) - XOF
        CALL SINVRT(TOPS,XOF,X,XP,S,N)      
        CALL SINVRT(BOTS,XOF,X,XP,S,N)      
C
      ENDIF
C
      TOPX = SEVAL(TOPS,X,XP,S,N)
      TOPY = SEVAL(TOPS,Y,YP,S,N)
      BOTX = SEVAL(BOTS,X,XP,S,N)
      BOTY = SEVAL(BOTS,Y,YP,S,N)
C
C
      HMOM = 0.
      HFX  = 0.
      HFY  = 0.
C
C---- integrate pressures on top and bottom sides of flap
      DO 20 I=2, N
        IF(S(I-1).GE.TOPS .AND. S(I).LE.BOTS) GO TO 20
C
         DX = X(I) - X(I-1)
         DY = Y(I) - Y(I-1)
         XMID = 0.5*(X(I)+X(I-1)) - XOF
         YMID = 0.5*(Y(I)+Y(I-1)) - YOF
         IF(LVISC) THEN
          PMID = 0.5*(CPV(I) + CPV(I-1))
         ELSE
          PMID = 0.5*(CPI(I) + CPI(I-1))
         ENDIF
         HMOM = HMOM + PMID*(XMID*DX + YMID*DY)
         HFX  = HFX  - PMID* DY
         HFY  = HFY  + PMID* DX
   20 CONTINUE
C
C---- find S(I)..S(I-1) interval containing s=TOPS
      DO I=2, N
        IF(S(I).GT.TOPS) GO TO 31
      ENDDO
C
   31 CONTINUE
C---- add on top surface chunk TOPS..S(I-1),  missed in the DO 20 loop.
      DX = TOPX - X(I-1)
      DY = TOPY - Y(I-1)
      XMID = 0.5*(TOPX+X(I-1)) - XOF
      YMID = 0.5*(TOPY+Y(I-1)) - YOF
      IF(S(I) .NE. S(I-1)) THEN
       FRAC = (TOPS-S(I-1))/(S(I)-S(I-1))
      ELSE
       FRAC = 0.
      ENDIF
      IF(LVISC) THEN
       TOPP = CPV(I)*FRAC + CPV(I-1)*(1.0-FRAC)
       PMID = 0.5*(TOPP+CPV(I-1))
      ELSE
       TOPP = CPI(I)*FRAC + CPI(I-1)*(1.0-FRAC)
       PMID = 0.5*(TOPP+CPI(I-1))
      ENDIF
      HMOM = HMOM + PMID*(XMID*DX + YMID*DY)
      HFX  = HFX  - PMID* DY
      HFY  = HFY  + PMID* DX
C
C---- add on inside flap surface contribution from hinge to top surface
      DX = XOF - TOPX
      DY = YOF - TOPY
      XMID = 0.5*(TOPX+XOF) - XOF
      YMID = 0.5*(TOPY+YOF) - YOF
      HMOM = HMOM + PMID*(XMID*DX + YMID*DY)
      HFX  = HFX  - PMID* DY
      HFY  = HFY  + PMID* DX
C
C---- find S(I)..S(I-1) interval containing s=BOTS
      DO I=N, 2, -1
        IF(S(I-1).LT.BOTS) GO TO 41
      ENDDO
C
   41 CONTINUE
C---- add on bottom surface chunk BOTS..S(I),  missed in the DO 20 loop.
      DX = X(I) - BOTX
      DY = Y(I) - BOTY
      XMID = 0.5*(BOTX+X(I)) - XOF
      YMID = 0.5*(BOTY+Y(I)) - YOF
      IF(S(I) .NE. S(I-1)) THEN
       FRAC = (BOTS-S(I-1))/(S(I)-S(I-1))
      ELSE
       FRAC = 0.
      ENDIF
      IF(LVISC) THEN
       BOTP = CPV(I)*FRAC + CPV(I-1)*(1.0-FRAC)
       PMID = 0.5*(BOTP+CPV(I))
      ELSE
       BOTP = CPI(I)*FRAC + CPI(I-1)*(1.0-FRAC)
       PMID = 0.5*(BOTP+CPI(I))
      ENDIF
      HMOM = HMOM + PMID*(XMID*DX + YMID*DY)
      HFX  = HFX  - PMID* DY
      HFY  = HFY  + PMID* DX
C
C---- add on inside flap surface contribution from hinge to bottom surface
      DX = BOTX - XOF
      DY = BOTY - YOF
      XMID = 0.5*(BOTX+XOF) - XOF
      YMID = 0.5*(BOTY+YOF) - YOF
      HMOM = HMOM + PMID*(XMID*DX + YMID*DY)
      HFX  = HFX  - PMID* DY
      HFY  = HFY  + PMID* DX
C
C---- add on TE base thickness contribution
      DX = X(1) - X(N)
      DY = Y(1) - Y(N)
      XMID = 0.5*(X(1)+X(N)) - XOF
      YMID = 0.5*(Y(1)+Y(N)) - YOF
      IF(LVISC) THEN
       PMID = 0.5*(CPV(1)+CPV(N))
      ELSE
       PMID = 0.5*(CPI(1)+CPI(N))
      ENDIF
      HMOM = HMOM + PMID*(XMID*DX + YMID*DY)
      HFX  = HFX  - PMID* DY
      HFY  = HFY  + PMID* DX
C
      RETURN
      END ! MHINGE


      SUBROUTINE VPAR
C---------------------------------------------
C     Viscous parameter change menu routine.
C---------------------------------------------
      INCLUDE 'XFOIL.INC'
      INCLUDE 'BLPAR.INC'
      CHARACTER*4 COMAND
      CHARACTER*128 COMARG
C
      DIMENSION IINPUT(20)
      DIMENSION RINPUT(20)
      LOGICAL ERROR
C
C
 10   TURB = 100.0 * EXP( -(ACRIT + 8.43)/2.4 )
      WRITE(*,1200) XSTRIP(1), XSTRIP(2), ACRIT, TURB, VACCEL,
     &              SCCON, DUXCON, GACON, GBCON, CTCON, CTRCON, CTRCEX
 1200 FORMAT(/' Xtr/c     =', F8.4, '    top    side'
     &       /' Xtr/c     =', F8.4, '    bottom side'
     &       /' Ncrit     =', F8.2, '   (', F6.3, ' % turb. level )'
     &       /' Vacc      =', F8.4,
     &      //' Klag  =', F8.4,'     Uxwt  =', F8.2
     &       /' A     =', F8.4,'     B     =', F8.4,'       KCt =', F8.5
     &       /' CtiniK=', F8.4,'     CtiniX=', F8.4 )
C
C======================================================================
C---- start of user interaction loop
  500 CONTINUE
      CALL ASKC('..VPAR^',COMAND,COMARG)
C
      DO I=1, 20
        IINPUT(I) = 0
        RINPUT(I) = 0.0
      ENDDO
      NINPUT = 20
      CALL GETINT(COMARG,IINPUT,NINPUT,ERROR)
      NINPUT = 20
      CALL GETFLT(COMARG,RINPUT,NINPUT,ERROR)
C
C--------------------------------------------------------------
      IF(COMAND.EQ.'    ') THEN
       RETURN
C
C--------------------------------------------------------------
      ELSEIF(COMAND.EQ.'?   ') THEN
       WRITE(*,1050)
 1050  FORMAT(
     &  /'   <cr>    Return to OPER menu'
     &  /'   SHOW    Display viscous parameters'
     &  /'   XTR  rr Change trip positions Xtr/c'
     &  /'   N    r  Change critical amplification exponent Ncrit'
     &  /'   VACC r  Change Newton solution acceleration parameter'
     &  /'   INIT    BL initialization flag toggle'
     & //'   LAG     change lag equation constants'
     &  /'   GB      change G-beta constants'
     &  /'   CTR     change initial transition-Ctau constants'
     &  /'   REST    restore BL calibration to baseline')
C
C--------------------------------------------------------------
      ELSEIF(COMAND.EQ.'SHOW') THEN
       GO TO 10
C
C--------------------------------------------------------------
      ELSEIF(COMAND.EQ.'XTR ') THEN
       IF(LPACC .AND. LVISC) THEN
        WRITE(*,2100)
        GO TO 500
       ENDIF
       IF(NINPUT.GE.2) THEN
        XSTRIP(1) = RINPUT(1)
        XSTRIP(2) = RINPUT(2)
       ELSE
        CALL ASKR('Enter top    side Xtrip/c^',XSTRIP(1))
        CALL ASKR('Enter bottom side Xtrip/c^',XSTRIP(2))
       ENDIF
       LVCONV = .FALSE.
C
C--------------------------------------------------------------
      ELSEIF(COMAND.EQ.'N   ') THEN
       IF(LPACC .AND. LVISC) THEN
        WRITE(*,2100)
        GO TO 500
       ENDIF
       IF(NINPUT.GE.1) THEN
        ACRIT = RINPUT(1)
       ELSE
        CALL ASKR('Enter critical amplification ratio^',ACRIT)
       ENDIF
       LVCONV = .FALSE.
C
C--------------------------------------------------------------
      ELSEIF(COMAND.EQ.'VACC') THEN
       IF(NINPUT.GE.1) THEN
        VACCEL = RINPUT(1)
       ELSE
        CALL ASKR('Enter viscous acceleration parameter^',VACCEL)
       ENDIF
C
C--------------------------------------------------------------
      ELSEIF(COMAND.EQ.'INIT') THEN
       LBLINI = .NOT.LBLINI
       IF(.NOT.LBLINI) WRITE(*,*)'BLs will be initialized on next point'
       IF(     LBLINI) WRITE(*,*)'BLs are assumed to be initialized'
       IF(.NOT.LBLINI) LIPAN = .FALSE.
C
C--------------------------------------------------------------
      ELSEIF(COMAND.EQ.'LAG ') THEN
       IF(LPACC .AND. LVISC) THEN
        WRITE(*,2100)
        GO TO 500
       ENDIF
       IF(NINPUT.GE.2) THEN
        SCCON  = RINPUT(1)
        DUXCON = RINPUT(2)
       ELSE
        CALL ASKR('Enter shear lag constant^',SCCON)
        CALL ASKR('Enter shear lag UxEQ weight^',DUXCON)
       ENDIF
C
C--------------------------------------------------------------
      ELSEIF(COMAND.EQ.'GB  ') THEN
       IF(LPACC .AND. LVISC) THEN
        WRITE(*,2100)
        GO TO 500
       ENDIF
       IF(NINPUT.GE.2) THEN
        GACON = RINPUT(1)
        GBCON = RINPUT(2)
       ELSE
        CALL ASKR('Enter G-beta constant A^',GACON)
        CALL ASKR('Enter G-beta constant B^',GBCON)
       ENDIF
       CTCON = 0.5/(GACON**2 * GBCON)
C
C--------------------------------------------------------------
      ELSEIF(COMAND.EQ.'CTR ') THEN
       IF(LPACC .AND. LVISC) THEN
        WRITE(*,2100)
        GO TO 500
       ENDIF
       IF(NINPUT.GE.2) THEN
        CTRCON = RINPUT(1)
        CTRCEX = RINPUT(2)
       ELSE
        CALL ASKR('Enter initial-Ctau constant^',CTRCON)
        CALL ASKR('Enter initial-Ctau exponent^',CTRCEX)
       ENDIF
C
C--------------------------------------------------------------
      ELSEIF(COMAND.EQ.'CFAC') THEN
       IF(NINPUT.GE.1) THEN
        CFFAC = RINPUT(1)
       ELSE
        CALL ASKR('Enter Cf scaling factor^',CFFAC)
       ENDIF
C
C--------------------------------------------------------------
      ELSEIF(COMAND.EQ.'REST') THEN
       IF(LPACC .AND. LVISC) THEN
        WRITE(*,2100)
        GO TO 500
       ENDIF
       CALL BLPINI
C
C--------------------------------------------------------------
      ELSE
       WRITE(*,1000) COMAND
 1000  FORMAT(1X,A4,' command not recognized.  Type a "?" for list')
C
      ENDIF
C
      GO TO 500
C--------------------------------------------
 2100 FORMAT(/' * Polar is being accumulated.'
     &       /' * Cannot change its parameters in midstream.')
      END ! VPAR




      SUBROUTINE SPECAL
C-----------------------------------
C     Converges to specified alpha.
C-----------------------------------
      INCLUDE 'XFOIL.INC'
      REAL MINF_CLM, MSQ_CLM
C
C---- calculate surface vorticity distributions for alpha = 0, 90 degrees
      IF(.NOT.LGAMU .OR. .NOT.LQAIJ) CALL GGCALC
C
      COSA = COS(ALFA)
      SINA = SIN(ALFA)
C
C---- superimpose suitably weighted  alpha = 0, 90  distributions
      DO 50 I=1, N
        GAM(I)   =  COSA*GAMU(I,1) + SINA*GAMU(I,2)
        GAM_A(I) = -SINA*GAMU(I,1) + COSA*GAMU(I,2)
   50 CONTINUE
      PSIO = COSA*GAMU(N+1,1) + SINA*GAMU(N+1,2)
C
      CALL TECALC
      CALL QISET
C
C---- set initial guess for the Newton variable CLM
      CLM = 1.0
C
C---- set corresponding  M(CLM), Re(CLM)
      CALL MRCL(CLM,MINF_CLM,REINF_CLM)
      CALL COMSET
C
C---- set corresponding CL(M)
      CALL CLCALC(N,X,Y,GAM,GAM_A,ALFA,MINF,QINF, XCMREF,YCMREF,
     &            CL,CM,CDP, CL_ALF,CL_MSQ)
C
C---- iterate on CLM
      DO 100 ITCL=1, 20
C
        MSQ_CLM = 2.0*MINF*MINF_CLM
        DCLM = (CL - CLM)/(1.0 - CL_MSQ*MSQ_CLM)
C
        CLM1 = CLM
        RLX = 1.0
C
C------ under-relaxation loop to avoid driving M(CL) above 1
        DO 90 IRLX=1, 12
C
          CLM = CLM1 + RLX*DCLM
C
C-------- set new freestream Mach M(CLM)
          CALL MRCL(CLM,MINF_CLM,REINF_CLM)
C
C-------- if Mach is OK, go do next Newton iteration
          IF(MATYP.EQ.1 .OR. MINF.EQ.0.0 .OR. MINF_CLM.NE.0.0) GO TO 91
C
          RLX = 0.5*RLX
   90   CONTINUE
   91   CONTINUE
C
C------ set new CL(M)
        CALL COMSET
        CALL CLCALC(N,X,Y,GAM,GAM_A,ALFA,MINF,QINF, XCMREF,YCMREF,
     &              CL,CM,CDP,CL_ALF,CL_MSQ)
C
        IF(ABS(DCLM).LE.1.0E-6) GO TO 110
C
  100 CONTINUE
      WRITE(*,*) 'SPECAL:  Minf convergence failed'
  110 CONTINUE
C
C---- set final Mach, CL, Cp distributions, and hinge moment
      CALL MRCL(CL,MINF_CL,REINF_CL)
      CALL COMSET
      CALL CLCALC(N,X,Y,GAM,GAM_A,ALFA,MINF,QINF, XCMREF,YCMREF,
     &            CL,CM,CDP, CL_ALF,CL_MSQ)
      WRITE(0,'(A,Z8,A,Z8,A,Z8,A,Z8)')
     & 'F_SPECAL CL=',TRANSFER(CL,1),
     & ' GAM1=',TRANSFER(GAM(1),1),
     & ' GAM40=',TRANSFER(GAM(40),1),
     & ' GAM80=',TRANSFER(GAM(80),1)
      CALL CPCALC(N,QINV,QINF,MINF,CPI)
      IF(LVISC) THEN
       CALL CPCALC(N+NW,QVIS,QINF,MINF,CPV)
       CALL CPCALC(N+NW,QINV,QINF,MINF,CPI)
      ELSE
       CALL CPCALC(N,QINV,QINF,MINF,CPI)
      ENDIF
      IF(LFLAP) CALL MHINGE
C
      RETURN
      END ! SPECAL
 
 
      SUBROUTINE SPECCL
C-----------------------------------------
C     Converges to specified inviscid CL.
C-----------------------------------------
      INCLUDE 'XFOIL.INC'
C
C---- calculate surface vorticity distributions for alpha = 0, 90 degrees
      IF(.NOT.LGAMU .OR. .NOT.LQAIJ) CALL GGCALC
C
C---- set freestream Mach from specified CL -- Mach will be held fixed
      CALL MRCL(CLSPEC,MINF_CL,REINF_CL)
      CALL COMSET
C
C---- current alpha is the initial guess for Newton variable ALFA
      COSA = COS(ALFA)
      SINA = SIN(ALFA)
      DO 10 I=1, N
        GAM(I)   =  COSA*GAMU(I,1) + SINA*GAMU(I,2)
        GAM_A(I) = -SINA*GAMU(I,1) + COSA*GAMU(I,2)
   10 CONTINUE
      PSIO = COSA*GAMU(N+1,1) + SINA*GAMU(N+1,2)
C
C---- get corresponding CL, CL_alpha, CL_Mach
      CALL CLCALC(N,X,Y,GAM,GAM_A,ALFA,MINF,QINF, XCMREF,YCMREF,
     &            CL,CM,CDP, CL_ALF,CL_MSQ)
      WRITE(0,'(A,Z8,A,Z8,A,Z8,A,Z8)')
     & 'F_SPECAL_CL CL=',TRANSFER(CL,1),
     & ' GAM1=',TRANSFER(GAM(1),1),
     & ' GAM40=',TRANSFER(GAM(40),1),
     & ' GAM80=',TRANSFER(GAM(80),1)
C
C---- Newton loop for alpha to get specified inviscid CL
      DO 100 ITAL=1, 20
C
        DALFA = (CLSPEC - CL) / CL_ALF
        RLX = 1.0
C
        ALFA = ALFA + RLX*DALFA
C
C------ set new surface speed distribution
        COSA = COS(ALFA)
        SINA = SIN(ALFA)
        DO 40 I=1, N
          GAM(I)   =  COSA*GAMU(I,1) + SINA*GAMU(I,2)
          GAM_A(I) = -SINA*GAMU(I,1) + COSA*GAMU(I,2)
   40   CONTINUE
        PSIO = COSA*GAMU(N+1,1) + SINA*GAMU(N+1,2)
C
C------ set new CL(alpha)
        CALL CLCALC(N,X,Y,GAM,GAM_A,ALFA,MINF,QINF, XCMREF,YCMREF,
     &              CL,CM,CDP,CL_ALF,CL_MSQ)
C
        IF(ABS(DALFA).LE.1.0E-6) GO TO 110
  100 CONTINUE
      WRITE(*,*) 'SPECCL:  CL convergence failed'
  110 CONTINUE
C
C---- set final surface speed and Cp distributions
      CALL TECALC
      CALL QISET
      IF(LVISC) THEN
       CALL CPCALC(N+NW,QVIS,QINF,MINF,CPV)
       CALL CPCALC(N+NW,QINV,QINF,MINF,CPI)
      ELSE
       CALL CPCALC(N,QINV,QINF,MINF,CPI)
      ENDIF
      IF(LFLAP) CALL MHINGE
C
      RETURN
      END ! SPECCL


      SUBROUTINE VISCAL(NITER1)
C----------------------------------------
C     Converges viscous operating point
C----------------------------------------
      INCLUDE 'XFOIL.INC'
      INCLUDE 'XBL.INC'
C
C---- convergence tolerance
      DATA EPS1 / 1.0E-4 /
C
      NITER = NITER1
C
C---- calculate wake trajectory from current inviscid solution if necessary
      IF(.NOT.LWAKE) THEN
       CALL XYWAKE
      ENDIF
C
C---- set velocities on wake from airfoil vorticity for alpha=0, 90
      CALL QWCALC
C
C---- set velocities on airfoil and wake for initial alpha
      CALL QISET
C
      IF(.NOT.LIPAN) THEN
C
       IF(LBLINI) CALL GAMQV
C
C----- locate stagnation point arc length position and panel index
       CALL STFIND
       WRITE(0,'(A,I4,A,Z8,A,Z8,A,I4)')
     &  'F_ISP isp=',IST,
     &  ' sst=',TRANSFER(SST,1),
     &  ' qinv_isp=',TRANSFER(QINV(IST),1),
     &  ' n=',N
       WRITE(0,'(A,I4,A,Z8,A,Z8)')
     &  'F_GAMU isp=',IST,
     &  ' G0=',TRANSFER(QINVU(IST,1),1),
     &  ' G1=',TRANSFER(QINVU(IST,2),1)
       DO 9191 JJGAM=1, N
         WRITE(0,'(A,I4,A,Z8,A,Z8)')
     &    'F_G I=',JJGAM,
     &    ' G0=',TRANSFER(QINVU(JJGAM,1),1),
     &    ' G1=',TRANSFER(QINVU(JJGAM,2),1)
 9191  CONTINUE
C
C----- set  BL position -> panel position  pointers
       CALL IBLPAN
C
C----- calculate surface arc length array for current stagnation point location
       CALL XICALC
C
C----- set  BL position -> system line  pointers
       CALL IBLSYS
C
      ENDIF
C
C---- set inviscid BL edge velocity UINV from QINV
      CALL UICALC
C
      IF(.NOT.LBLINI) THEN
C
C----- set initial Ue from inviscid Ue
       DO IBL=1, NBL(1)
         UEDG(IBL,1) = UINV(IBL,1)
       ENDDO
C
       DO IBL=1, NBL(2)
         UEDG(IBL,2) = UINV(IBL,2)
       ENDDO
C
      ENDIF
C---- GDB: dump UEDG at key stations before first SETBL
      WRITE(0,'(A,Z8,A,Z8,A,Z8,A,Z8)')
     &  'F_UEDG s1_57=',TRANSFER(UEDG(57,1),1),
     &  ' s1_58=',TRANSFER(UEDG(58,1),1),
     &  ' s1_96=',TRANSFER(UEDG(96,1),1),
     &  ' s1_97=',TRANSFER(UEDG(97,1),1)
C
      IF(LVCONV) THEN
C----- set correct CL if converged point exists
       CALL QVFUE
       IF(LVISC) THEN
        CALL CPCALC(N+NW,QVIS,QINF,MINF,CPV)
        CALL CPCALC(N+NW,QINV,QINF,MINF,CPI)
       ELSE
        CALL CPCALC(N,QINV,QINF,MINF,CPI)
       ENDIF
       CALL GAMQV
       CALL CLCALC(N,X,Y,GAM,GAM_A,ALFA,MINF,QINF, XCMREF,YCMREF,
     &             CL,CM,CDP, CL_ALF,CL_MSQ)
       WRITE(0,'(A,Z8,A,Z8,A,Z8,A,Z8)')
     &  'F_CLCALC CL=',TRANSFER(CL,1),
     &  ' GAM1=',TRANSFER(GAM(1),1),
     &  ' GAM40=',TRANSFER(GAM(40),1),
     &  ' GAM80=',TRANSFER(GAM(80),1)
       WRITE(0,'(A,I4,A,Z8,A,Z8)')
     &  'F_WAKE_END NBL2=',NBL(2),
     &  ' THW=',TRANSFER(THET(NBL(2),2),1),
     &  ' DSW=',TRANSFER(DSTR(NBL(2),2),1)
       CALL CDCALC
      ENDIF
C
C---- set up source influence matrix if it doesn't exist
      IBIJH = 0
      DO 8811 IBIJR=1, N
        DO 8812 IBIJC=1, N
          IBIJH = IEOR(IBIJH, TRANSFER(BIJ(IBIJR,IBIJC),1))
 8812   CONTINUE
 8811 CONTINUE
      ILUH = 0
      DO 8821 ILUR=1, N+1
        DO 8822 ILUC=1, N+1
          ILUH = IEOR(ILUH, TRANSFER(AIJ(ILUR,ILUC),1))
 8822   CONTINUE
 8821 CONTINUE
      WRITE(0,'(A,Z8,A,Z8)') 'F_BIJ hash=',IBIJH,
     & ' LU=',ILUH
      IF(.NOT.LWDIJ .OR. .NOT.LADIJ) CALL QDCALC
C---- Hash first column of DIJ after QDCALC
      ICOL0 = 0
      DO 8831 IC0=1, N
        ICOL0 = IEOR(ICOL0, TRANSFER(DIJ(IC0,1),1))
 8831 CONTINUE
      WRITE(0,'(A,Z8)') 'F_DIJ col0=',ICOL0
      IDIJH = 0
      DO 8801 IDIJR=1, N+NW
        IDIJRH = 0
        DO 8802 IDIJC=1, N+NW
          IDIJH = IEOR(IDIJH, TRANSFER(DIJ(IDIJR,IDIJC),1))
          IDIJRH = IEOR(IDIJRH, TRANSFER(DIJ(IDIJR,IDIJC),1))
 8802   CONTINUE
        IF(IDIJR.LE.5 .OR. (IDIJR.GE.161.AND.IDIJR.LE.165)) THEN
         WRITE(0,'(A,I3,A,Z8)') 'F_DIJ_ROW r=',IDIJR-1,
     &    ' hash=',IDIJRH
        ENDIF
 8801 CONTINUE
      IDIJAH = 0
      DO 8841 IDIJA=1, N
        DO 8842 IDIJB=1, N
          IDIJAH = IEOR(IDIJAH, TRANSFER(DIJ(IDIJA,IDIJB),1))
 8842   CONTINUE
 8841 CONTINUE
      WRITE(0,'(A,Z8,A,Z8)')
     & 'F_DIJ hash=',IDIJH,' airfoil=',IDIJAH
C
C---- Newton iteration for entire BL solution
      IF(NITER.EQ.0) CALL ASKI('Enter number of iterations^',NITER)
      WRITE(*,*)
      WRITE(*,*) 'Solving BL system ...'
      TRACE_OUTER = 0
      DO 1000 ITER=1, NITER
C
C------ Dump initial UEDG/THET/DSTR for first 8 stations both sides
        IF (ITER.EQ.1) THEN
          DO 7997 ISP_=1, 2
            DO 7996 IBP=1, 8
              IF (IBP.LE.NBL(ISP_)) THEN
                WRITE(0,'(A,I1,A,I2,3(A,Z8))') 'F_INIT s=',ISP_,
     &           ' ibl=',IBP,
     &           ' UEDG=',TRANSFER(UEDG(IBP,ISP_),1),
     &           ' THET=',TRANSFER(THET(IBP,ISP_),1),
     &           ' DSTR=',TRANSFER(DSTR(IBP,ISP_),1)
              ENDIF
 7996       CONTINUE
 7997     CONTINUE
        ENDIF
C------ BL state hash before SETBL
        IBLH = 0
        DO 7998 ISH=1, 2
          DO 7999 IBH=1, NBL(ISH)
            IBLH = IBLH +
     &        IAND(TRANSFER(THET(IBH,ISH),1), 2147483647)
            IBLH = IBLH +
     &        IAND(TRANSFER(DSTR(IBH,ISH),1), 2147483647)
            IBLH = IBLH +
     &        IAND(TRANSFER(UEDG(IBH,ISH),1), 2147483647)
 7999     CONTINUE
 7998   CONTINUE
        WRITE(0,'(A,I2,A,Z8)') 'F_BLS',ITER,' H=',IBLH
C------ set outer iteration for TRCHEK2 traces
        TRACE_OUTER = ITER
C------ fill Newton system for BL variables
        CALL SETBL
        IF (ITER.EQ.1) THEN
          DO 7891 ISP_=1, 2
            DO 7890 IBP=1, 8
              IF (IBP.LE.NBL(ISP_)) THEN
                WRITE(0,'(A,I1,A,I2,3(A,Z8))') 'F_POSTSETBL s=',ISP_,
     &           ' ibl=',IBP,
     &           ' UEDG=',TRANSFER(UEDG(IBP,ISP_),1),
     &           ' THET=',TRANSFER(THET(IBP,ISP_),1),
     &           ' DSTR=',TRANSFER(DSTR(IBP,ISP_),1)
                WRITE(0,'(A,I1,A,I2,2(A,Z8))') 'F_UM s=',ISP_,
     &           ' ibl=',IBP,
     &           ' UINV=',TRANSFER(UINV(IBP,ISP_),1),
     &           ' MASS=',TRANSFER(MASS(IBP,ISP_),1)
              ENDIF
 7890       CONTINUE
 7891     CONTINUE
C--- Wake station dump (side 2 around first wake = JBL=65)
          DO 7892 IBPW=63, 67
            IF (IBPW.LE.NBL(2)) THEN
              WRITE(0,'(A,I2,4(A,Z8))') 'F_WAKE2 ibl=',IBPW,
     &         ' UEDG=',TRANSFER(UEDG(IBPW,2),1),
     &         ' THET=',TRANSFER(THET(IBPW,2),1),
     &         ' DSTR=',TRANSFER(DSTR(IBPW,2),1),
     &         ' MASS=',TRANSFER(MASS(IBPW,2),1)
            ENDIF
 7892     CONTINUE
        ENDIF
        WRITE(0,'(A,I2,A,I4,A,I4,A,Z8,A,Z8)')
     &   'F_ITRAN it=',ITER,
     &   ' s1=',ITRAN(1),
     &   ' s2=',ITRAN(2),
     &   ' xtr1=',TRANSFER(XOCTR(1),1),
     &   ' xtr2=',TRANSFER(XOCTR(2),1)
C------ dump VDEL RHS at system lines 3-4 BEFORE BLSOLV
        WRITE(0,'(A,Z8,1X,Z8,1X,Z8,1X,Z8,1X,Z8,1X,Z8)')
     & 'F_RHS3  ',
     & TRANSFER(VDEL(1,1,3),1),TRANSFER(VDEL(2,1,3),1),
     & TRANSFER(VDEL(3,1,3),1),
     & TRANSFER(VDEL(1,2,3),1),TRANSFER(VDEL(2,2,3),1),
     & TRANSFER(VDEL(3,2,3),1)
        WRITE(0,'(A,Z8,1X,Z8,1X,Z8,1X,Z8,1X,Z8,1X,Z8)')
     & 'F_RHS4  ',
     & TRANSFER(VDEL(1,1,4),1),TRANSFER(VDEL(2,1,4),1),
     & TRANSFER(VDEL(3,1,4),1),
     & TRANSFER(VDEL(1,2,4),1),TRANSFER(VDEL(2,2,4),1),
     & TRANSFER(VDEL(3,2,4),1)
C------ Per-station full PRE-BLSOLV VDEL dump (iter 1 only)
        IF(ITER.EQ.1) THEN
          DO 7919 JVV=1, NSYS
            WRITE(0,'(A,I3,1X,Z8,1X,Z8,1X,Z8)')
     &       'F_PREVDEL it=1 jv=',JVV,
     &       TRANSFER(VDEL(1,1,JVV),1),
     &       TRANSFER(VDEL(2,1,JVV),1),
     &       TRANSFER(VDEL(3,1,JVV),1)
            WRITE(0,'(A,I3,1X,Z8,1X,Z8,1X,Z8)')
     &       'F_PREVDEL2 it=1 jv=',JVV,
     &       TRANSFER(VDEL(1,2,JVV),1),
     &       TRANSFER(VDEL(2,2,JVV),1),
     &       TRANSFER(VDEL(3,2,JVV),1)
 7919     CONTINUE
        ENDIF
C------ VM checksum (XOR hash) for parity check - ALL iterations
          WRITE(0,'(A,I3,A,I4)') 'F_NSYS_AT_ITER it=',ITER,
     &     ' NSYS=', NSYS
          IVMH = 0
          IVAH = 0
          IVBH = 0
          IVDH = 0
          DO 7902 IVV=1, NSYS
            DO 7903 JVV=1, NSYS
              DO 7904 KK=1, 3
                IVMH = IEOR(IVMH, TRANSFER(VM(KK,JVV,IVV),1))
 7904         CONTINUE
 7903       CONTINUE
            DO 7905 KK=1, 3
              IVAH = IEOR(IVAH, TRANSFER(VA(KK,1,IVV),1))
              IVAH = IEOR(IVAH, TRANSFER(VA(KK,2,IVV),1))
              IVBH = IEOR(IVBH, TRANSFER(VB(KK,1,IVV),1))
              IVBH = IEOR(IVBH, TRANSFER(VB(KK,2,IVV),1))
              IVDH = IEOR(IVDH, TRANSFER(VDEL(KK,1,IVV),1))
              IVDH = IEOR(IVDH, TRANSFER(VDEL(KK,2,IVV),1))
 7905       CONTINUE
 7902     CONTINUE
C------ Additive checksums (immune to sign-of-zero)
          IVMS = 0
          IVDS = 0
          IVD1S = 0
          IVD2S = 0
          DO 7912 IVV=1, NSYS
            DO 7913 JVV=1, NSYS
              DO 7914 KK=1, 3
                IVMS = IVMS + IAND(TRANSFER(VM(KK,JVV,IVV),1),
     &                              2147483647)
 7914         CONTINUE
 7913       CONTINUE
            DO 7915 KK=1, 3
              IVD1S = IVD1S + IAND(TRANSFER(VDEL(KK,1,IVV),1),
     &                              2147483647)
              IVD2S = IVD2S + IAND(TRANSFER(VDEL(KK,2,IVV),1),
     &                              2147483647)
 7915       CONTINUE
 7912     CONTINUE
          IVDS = IVD1S + IVD2S
          WRITE(0,'(A,I2,A,Z8,A,Z8,A,Z8,A,Z8,
     &     A,Z12.12,A,Z12.12,A,Z12.12,A,Z12.12)')
     &     'F_MHASH ',ITER,
     &     ' VM=',IVMH,' VA=',IVAH,' VB=',IVBH,
     &     ' VD=',IVDH,
     &     ' VMs=',IVMS,' VDs=',IVDS,
     &     ' VD1s=',IVD1S,' VD2s=',IVD2S
        IF(ITER.EQ.3) THEN
          WRITE(0,'(A,I4)') 'F_VB3_LOOP_START NSYS=',NSYS
          DO 7909 IVV=1, NSYS
            WRITE(0,'(A,I4,6(1X,Z8))')
     &       'F_VB3 ',IVV,
     &       TRANSFER(VB(1,1,IVV),1),TRANSFER(VB(1,2,IVV),1),
     &       TRANSFER(VB(2,1,IVV),1),TRANSFER(VB(2,2,IVV),1),
     &       TRANSFER(VB(3,1,IVV),1),TRANSFER(VB(3,2,IVV),1)
 7909     CONTINUE
          WRITE(0,'(A)') 'F_VB3_LOOP_END'
        ENDIF
C------ Per-station VM hash at iter 1 (find divergent station in first step)
        IF(ITER.EQ.1) THEN
          DO 8918 IVV=1, NSYS
            IVMX = 0
            IVMS = 0
            DO 8925 JVV=1, NSYS
              DO 8935 KK=1, 3
                ITMP = TRANSFER(REAL(VM(KK,JVV,IVV)),1)
                IVMX = IEOR(IVMX, ITMP)
                IVMS = IVMS + IAND(ITMP, 2147483647)
 8935         CONTINUE
 8925       CONTINUE
            WRITE(0,'(A,I4,A,Z8,A,Z8)')
     &       'F_VMS1 iv=',IVV,' xor=',IVMX,' sum=',IVMS
 8918     CONTINUE
        ENDIF
C------ Per-station VM hash at iter 5 (find divergent station)
        IF(ITER.EQ.5) THEN
          DO 8919 IVV=1, NSYS
            IVMX = 0
            IVMS = 0
            DO 8920 JVV=1, NSYS
              DO 8930 KK=1, 3
                ITMP = TRANSFER(REAL(VM(KK,JVV,IVV)),1)
                IVMX = IEOR(IVMX, ITMP)
                IVMS = IVMS + IAND(ITMP, 2147483647)
 8930         CONTINUE
 8920       CONTINUE
            WRITE(0,'(A,I4,A,Z8,A,Z8)')
     &       'F_VMS5 iv=',IVV,' xor=',IVMX,' sum=',IVMS
 8919     CONTINUE
C-------- Dump VM[*,*,77] per-jv for divergence localization
          DO 8921 JVV=1, NSYS
            IB0 = TRANSFER(REAL(VM(1,JVV,77)),1)
            IB1 = TRANSFER(REAL(VM(2,JVV,77)),1)
            IB2 = TRANSFER(REAL(VM(3,JVV,77)),1)
            IF(IB0.NE.0 .OR. IB1.NE.0 .OR. IB2.NE.0) THEN
              WRITE(0,'(A,I4,A,Z8,A,Z8,A,Z8)')
     &         'F_VM77 jv=',JVV,' r1=',IB0,' r2=',IB1,' r3=',IB2
            ENDIF
 8921     CONTINUE
          WRITE(0,'(A,A,Z8,A,Z8,A,Z8,A,Z8,A,Z8,A,Z8,A,Z8,A,Z8,
     &             A,Z8,A,Z8,A,Z8,A,Z8,A,Z8,A,Z8,A,Z8)')
     &     'F_VABD77',
     &     ' VA11=',TRANSFER(REAL(VA(1,1,77)),1),
     &     ' VA12=',TRANSFER(REAL(VA(1,2,77)),1),
     &     ' VA21=',TRANSFER(REAL(VA(2,1,77)),1),
     &     ' VA22=',TRANSFER(REAL(VA(2,2,77)),1),
     &     ' VA31=',TRANSFER(REAL(VA(3,1,77)),1),
     &     ' VA32=',TRANSFER(REAL(VA(3,2,77)),1),
     &     ' VB11=',TRANSFER(REAL(VB(1,1,77)),1),
     &     ' VB12=',TRANSFER(REAL(VB(1,2,77)),1),
     &     ' VB21=',TRANSFER(REAL(VB(2,1,77)),1),
     &     ' VB22=',TRANSFER(REAL(VB(2,2,77)),1),
     &     ' VB31=',TRANSFER(REAL(VB(3,1,77)),1),
     &     ' VB32=',TRANSFER(REAL(VB(3,2,77)),1),
     &     ' VD11=',TRANSFER(REAL(VDEL(1,1,77)),1),
     &     ' VD21=',TRANSFER(REAL(VDEL(2,1,77)),1),
     &     ' VD31=',TRANSFER(REAL(VDEL(3,1,77)),1)
        ENDIF
        IF(ITER.EQ.1) THEN
          WRITE(*,'(A,Z8,A,Z8,A,Z8)')
     &     'F_MATRIX_HASH VM=',IVMH,' VA=',IVAH,' VB=',IVBH
C-------- VB region hashes: surface (1..77), TE (78..80), wake (81+)
          IVBSURF = 0
          IVBTE = 0
          IVBWAKE = 0
          DO 7906 IVV=1, NSYS
            IHH = 0
            DO 7907 KK=1, 3
              IHH = IEOR(IHH, TRANSFER(VB(KK,1,IVV),1))
              IHH = IEOR(IHH, TRANSFER(VB(KK,2,IVV),1))
 7907       CONTINUE
            IF(IVV.LE.77) THEN
              IVBSURF = IEOR(IVBSURF, IHH)
            ELSE IF(IVV.LE.80) THEN
              IVBTE = IEOR(IVBTE, IHH)
            ELSE
              IVBWAKE = IEOR(IVBWAKE, IHH)
            ENDIF
 7906     CONTINUE
          WRITE(*,'(A,Z8,A,Z8,A,Z8)')
     &     'F_VB_REGION surf=',IVBSURF,' te=',IVBTE,
     &     ' wake=',IVBWAKE
        ENDIF
C------ dump ALL post-BLSOLV VDEL (first iteration only)
        IF(ITER.EQ.1) THEN
          DO 7901 JVV=1, NSYS
            WRITE(0,'(A,I4,1X,Z8,1X,Z8,1X,Z8)')
     &       'F_SOLVED jv=',JVV,
     &       TRANSFER(VDEL(1,1,JVV),1),
     &       TRANSFER(VDEL(2,1,JVV),1),
     &       TRANSFER(VDEL(3,1,JVV),1)
 7901     CONTINUE
        ENDIF
C------ dump VM at system lines 3-4 BEFORE BLSOLV (2x2 diag block)
        WRITE(0,'(A,Z8,1X,Z8,1X,Z8,1X,Z8)')
     & 'F_VM3   ',
     & TRANSFER(VM(1,1,3),1),TRANSFER(VM(1,2,3),1),
     & TRANSFER(VM(2,1,3),1),TRANSFER(VM(2,2,3),1)
        WRITE(0,'(A,Z8,1X,Z8,1X,Z8,1X,Z8)')
     & 'F_VM4   ',
     & TRANSFER(VM(1,1,4),1),TRANSFER(VM(1,2,4),1),
     & TRANSFER(VM(2,1,4),1),TRANSFER(VM(2,2,4),1)
C------ dump VA at system lines 3-4 BEFORE BLSOLV (2x2 sub-diag block)
        WRITE(0,'(A,Z8,1X,Z8,1X,Z8,1X,Z8)')
     & 'F_VA3   ',
     & TRANSFER(VA(1,1,3),1),TRANSFER(VA(1,2,3),1),
     & TRANSFER(VA(2,1,3),1),TRANSFER(VA(2,2,3),1)
        WRITE(0,'(A,Z8,1X,Z8,1X,Z8,1X,Z8)')
     & 'F_VA4   ',
     & TRANSFER(VA(1,1,4),1),TRANSFER(VA(1,2,4),1),
     & TRANSFER(VA(2,1,4),1),TRANSFER(VA(2,2,4),1)
C------ dump VB at system lines 3-4 BEFORE BLSOLV (2x2 super-diag block)
        WRITE(0,'(A,Z8,1X,Z8,1X,Z8,1X,Z8)')
     & 'F_VB3   ',
     & TRANSFER(VB(1,1,3),1),TRANSFER(VB(1,2,3),1),
     & TRANSFER(VB(2,1,3),1),TRANSFER(VB(2,2,3),1)
        WRITE(0,'(A,Z8,1X,Z8,1X,Z8,1X,Z8)')
     & 'F_VB4   ',
     & TRANSFER(VB(1,1,4),1),TRANSFER(VB(1,2,4),1),
     & TRANSFER(VB(2,1,4),1),TRANSFER(VB(2,2,4),1)
C
C------ detect negative zero in VDEL (all iterations)
         DO 7931 IVD=1, NSYS
           DO 7932 IKK=1, 3
             DO 7933 ICC=1, 2
               IF(VDEL(IKK,ICC,IVD).EQ.0.0 .AND.
     &            TRANSFER(VDEL(IKK,ICC,IVD),1).NE.0) THEN
                WRITE(0,'(A,I2,A,I4,A,I1,A,I1,A,Z8)')
     &           'F_NEG0 it=',ITER,' iv=',IVD,
     &           ' k=',IKK,' c=',ICC,
     &           ' bits=',TRANSFER(VDEL(IKK,ICC,IVD),1)
               ENDIF
 7933        CONTINUE
 7932      CONTINUE
 7931    CONTINUE
C------ Per-station VM additive checksum at iteration 14
        IF(ITER.EQ.14) THEN
         DO 7971 IVCK1=1, NSYS
           ISCK = 0
           DO 7972 IVCK2=1, NSYS
             DO 7973 IVCK3=1, 3
               ISCK = ISCK + TRANSFER(VM(IVCK3,IVCK2,IVCK1),1)
 7973        CONTINUE
 7972      CONTINUE
           WRITE(0,'(A,I4,A,Z8)')
     &      'F_VMS13 iv=',IVCK1,' sum=',ISCK
 7971    CONTINUE
        ENDIF
C------ pre-BLSOLV VDEL column 1 at iteration 14 ALL stations
        IF(ITER.EQ.14) THEN
         DO 7961 IVD14=1, NSYS
           WRITE(0,'(A,I4,3(1X,Z8))')
     &      'F_VD13 jv=',IVD14,
     &      TRANSFER(VDEL(1,1,IVD14),1),
     &      TRANSFER(VDEL(2,1,IVD14),1),
     &      TRANSFER(VDEL(3,1,IVD14),1)
 7961    CONTINUE
        ENDIF
C------ pre-BLSOLV VDEL trace (ALL entries, iter 1 only)
        IF(ITER.EQ.1) THEN
         DO 7921 IVD=1, NSYS
           WRITE(0,'(A,I4,3(1X,Z8))') 'F_VDEL jv=',IVD,
     &      TRANSFER(VDEL(1,1,IVD),1),
     &      TRANSFER(VDEL(2,1,IVD),1),
     &      TRANSFER(VDEL(3,1,IVD),1)
 7921    CONTINUE
        ENDIF
C------ pre-BLSOLV aggregate checksum
        IVMAGG = 0
        IVDAGG = 0
        DO 7991 IVAG=1, NSYS
          DO 7992 KKAG=1, 3
            DO 7993 JVAG=1, NSYS
              IVMAGG = IVMAGG +
     &          IAND(TRANSFER(VM(KKAG,JVAG,IVAG),1), 2147483647)
 7993       CONTINUE
            IVDAGG = IVDAGG +
     &        IAND(TRANSFER(VDEL(KKAG,1,IVAG),1), 2147483647)
 7992     CONTINUE
 7991   CONTINUE
        WRITE(0,'(A,I2,A,Z8,A,Z8)')
     &   'F_PRE_BL',ITER,' VM=',IVMAGG,' VD=',IVDAGG
C------ dump all RHS VDEL BEFORE BLSOLV for ah79 debug
        IF(ITER.EQ.33) THEN
         DO 9801 KAH=1, NSYS
           WRITE(0,'(A,I4,A,Z8,A,Z8,A,Z8,A,Z8,A,Z8,A,Z8)')
     &      'F_VDEL_PRE jv=',KAH,
     &      ' V11=',TRANSFER(VDEL(1,1,KAH),1),
     &      ' V21=',TRANSFER(VDEL(2,1,KAH),1),
     &      ' V31=',TRANSFER(VDEL(3,1,KAH),1),
     &      ' V12=',TRANSFER(VDEL(1,2,KAH),1),
     &      ' V22=',TRANSFER(VDEL(2,2,KAH),1),
     &      ' V32=',TRANSFER(VDEL(3,2,KAH),1)
 9801    CONTINUE
        ENDIF
C------ solve Newton system with custom solver
        CALL BLSOLV
C------ post-BLSOLV additive checksum
        ISOL1 = 0
        ISOL2 = 0
        DO 7940 IVV=1, NSYS
          DO 7941 KK=1, 3
            ISOL1 = ISOL1 + IAND(TRANSFER(VDEL(KK,1,IVV),1),
     &                            2147483647)
            ISOL2 = ISOL2 + IAND(TRANSFER(VDEL(KK,2,IVV),1),
     &                            2147483647)
 7941     CONTINUE
 7940   CONTINUE
        WRITE(0,'(A,I2,A,Z8,A,Z8)')
     &   'F_BLSOLV',ITER,' SOL1=',ISOL1,' SOL2=',ISOL2
C------ dump post-BLSOLV VDEL solution (ALL stations, iters 1-2)
        IF(ITER.LE.2) THEN
          DO 7908 JVV=1, NSYS
            WRITE(0,781) ITER,JVV,
     &       TRANSFER(VDEL(1,1,JVV),1),
     &       TRANSFER(VDEL(2,1,JVV),1),
     &       TRANSFER(VDEL(3,1,JVV),1)
 781        FORMAT('F_VDSOL it=',I1,' jv=',I3,3(1X,Z8.8))
            WRITE(0,782) ITER,JVV,
     &       TRANSFER(VDEL(1,2,JVV),1),
     &       TRANSFER(VDEL(2,2,JVV),1),
     &       TRANSFER(VDEL(3,2,JVV),1)
 782        FORMAT('F_VDSOL2 it=',I1,' jv=',I3,3(1X,Z8.8))
 7908     CONTINUE
        ENDIF
C------ GDB: dump VM at first system line BEFORE BLSOLV
        WRITE(0,'(A,Z8,A,Z8,A,Z8,A,Z8)')
     &   'F_VM_PRE VM110=',TRANSFER(VM(1,1,1),1),
     &   ' VM120=',TRANSFER(VM(1,2,1),1),
     &   ' VM210=',TRANSFER(VM(2,1,1),1),
     &   ' VM220=',TRANSFER(VM(2,2,1),1)
        WRITE(0,'(A,Z8,A,Z8,A,Z8,A,Z8)')
     &   'F_VA_PRE VA110=',TRANSFER(VA(1,1,1),1),
     &   ' VA120=',TRANSFER(VA(1,2,1),1),
     &   ' VB110=',TRANSFER(VB(1,1,1),1),
     &   ' VB120=',TRANSFER(VB(1,2,1),1)
C------ dump VDEL at system lines 3-4 after BLSOLV (both columns)
        WRITE(0,'(A,Z8,1X,Z8,1X,Z8,1X,Z8,1X,Z8,1X,Z8)')
     & 'F_VDEL3 ',
     & TRANSFER(VDEL(1,1,3),1),TRANSFER(VDEL(2,1,3),1),
     & TRANSFER(VDEL(3,1,3),1),
     & TRANSFER(VDEL(1,2,3),1),TRANSFER(VDEL(2,2,3),1),
     & TRANSFER(VDEL(3,2,3),1)
        WRITE(0,'(A,Z8,1X,Z8,1X,Z8,1X,Z8,1X,Z8,1X,Z8)')
     & 'F_VDEL4 ',
     & TRANSFER(VDEL(1,1,4),1),TRANSFER(VDEL(2,1,4),1),
     & TRANSFER(VDEL(3,1,4),1),
     & TRANSFER(VDEL(1,2,4),1),TRANSFER(VDEL(2,2,4),1),
     & TRANSFER(VDEL(3,2,4),1)
C
C------ dump all VDEL for iter 32/33 pre-UPDATE
        IF(ITER.EQ.32 .OR. ITER.EQ.33) THEN
         DO 9701 KAH=1, NSYS
           WRITE(0,'(A,I3,A,I4,A,Z8,A,Z8,A,Z8,A,Z8,A,Z8,A,Z8)')
     &      'F_VDEL_ALL it=',ITER,' jv=',KAH,
     &      ' V11=',TRANSFER(VDEL(1,1,KAH),1),
     &      ' V21=',TRANSFER(VDEL(2,1,KAH),1),
     &      ' V31=',TRANSFER(VDEL(3,1,KAH),1),
     &      ' V12=',TRANSFER(VDEL(1,2,KAH),1),
     &      ' V22=',TRANSFER(VDEL(2,2,KAH),1),
     &      ' V32=',TRANSFER(VDEL(3,2,KAH),1)
 9701    CONTINUE
        ENDIF
C------ update BL variables
        CALL UPDATE
        IF(ITER.EQ.32 .OR. ITER.EQ.33) THEN
         DO 9601 IAH=1, 2
          DO 9602 IAHB=2, NBL(IAH)
           WRITE(0,'(A,I3,A,I1,A,I4,A,Z8,A,Z8,A,Z8,A,Z8)')
     &      'F_POSTUPD_FULL it=',ITER,
     &      ' s=',IAH,' i=',IAHB,
     &      ' T=',TRANSFER(THET(IAHB,IAH),1),
     &      ' D=',TRANSFER(DSTR(IAHB,IAH),1),
     &      ' U=',TRANSFER(UEDG(IAHB,IAH),1),
     &      ' C=',TRANSFER(CTAU(IAHB,IAH),1)
 9602     CONTINUE
 9601    CONTINUE
        ENDIF
        IF(ITER.GE.20 .AND. ITER.LE.23) THEN
         DO 7854 ISNS=1, 2
          DO 7855 INS=2, NBL(ISNS)
           IF(INS.LE.5 .OR. (ISNS.EQ.2 .AND. INS.GE.89)) THEN
            WRITE(0,'(A,I2,A,I1,A,I3,A,Z8,A,Z8,A,Z8,A,Z8)')
     &       'F_POSTUPDATE it=',ITER,' s=',ISNS,' i=',INS,
     &       ' C=',TRANSFER(CTAU(INS,ISNS),1),
     &       ' T=',TRANSFER(THET(INS,ISNS),1),
     &       ' D=',TRANSFER(DSTR(INS,ISNS),1),
     &       ' U=',TRANSFER(UEDG(INS,ISNS),1)
           ENDIF
 7855     CONTINUE
 7854    CONTINUE
        ENDIF
C
        IF(LALFA) THEN
C------- set new freestream Mach, Re from new CL
         CALL MRCL(CL,MINF_CL,REINF_CL)
         CALL COMSET
        ELSE
C------- set new inviscid speeds QINV and UINV for new alpha
         CALL QISET
         CALL UICALC
        ENDIF
C
C------ calculate edge velocities QVIS(.) from UEDG(..)
        CALL QVFUE
C------ GDB: trace QVIS post-QVFUE for iter 1
        IF(ITER.EQ.1) THEN
         WRITE(0,'(A,Z8,A,Z8,A,Z8,A,Z8)')
     &   'F_QVIS q1=',TRANSFER(QVIS(1),1),
     &   ' q2=',TRANSFER(QVIS(2),1),
     &   ' q40=',TRANSFER(QVIS(40),1),
     &   ' q80=',TRANSFER(QVIS(80),1)
        ENDIF
C
C------ set GAM distribution from QVIS
        CALL GAMQV
C
C------ relocate stagnation point
        ISTOLD = IST
        CALL STMOVE
        IF(IST.NE.ISTOLD) THEN
         WRITE(0,'(A,I2,A,I4,A,I4)')
     &    'F_STMOVE it=',ITER,' old=',ISTOLD,' new=',IST
        ENDIF
C------ post-STMOVE trace for iter 11 at wake stations 64-70 side 2
        IF(ITER.EQ.11) THEN
         DO 7795 IBLPS=64, 70
          WRITE(0,'(A,I3,A,Z8,A,Z8,A,Z8,A,Z8)')
     &     'F_POST_STMOVE11 IBL=',IBLPS,
     &     ' T=',TRANSFER(THET(IBLPS,2),1),
     &     ' D=',TRANSFER(DSTR(IBLPS,2),1),
     &     ' U=',TRANSFER(UEDG(IBLPS,2),1),
     &     ' M=',TRANSFER(MASS(IBLPS,2),1)
 7795    CONTINUE
        ENDIF
C
C------ set updated CL,CD
        CALL CLCALC(N,X,Y,GAM,GAM_A,ALFA,MINF,QINF, XCMREF,YCMREF,
     &              CL,CM,CDP,CL_ALF,CL_MSQ)
        WRITE(0,'(A,I2,A,Z8)') 'F_CLCALC it=',ITER,
     &   ' CL=',TRANSFER(CL,1)
        CALL CDCALC
        WRITE(0,'(A,I2,A,Z8,A,Z8)') 'F_ITER_CDCL it=',ITER,
     &   ' CD=',TRANSFER(CD,1),' CL=',TRANSFER(CL,1)
        WRITE(0,'(A,I2,A,Z8,A,Z8)') 'F_RMSBL it=',ITER,
     &   ' rmsbl=',TRANSFER(RMSBL,1),' eps1=',TRANSFER(EPS1,1)
C------ trace wake end state to find where BL state divergence enters
        WRITE(0,'(A,I2,A,Z8,A,Z8,A,Z8)') 'F_WAKEEND it=',ITER,
     &   ' T=',TRANSFER(THET(NBL(2),2),1),
     &   ' D=',TRANSFER(DSTR(NBL(2),2),1),
     &   ' U=',TRANSFER(UEDG(NBL(2),2),1)
C------ trace stations 2-5 (near stagnation) for both sides
        DO 7851 ISTNS=1, 2
          DO 7852 IBLNS=2, 5
           IF(IBLNS.LE.NBL(ISTNS)) THEN
            WRITE(0,'(A,I2,A,I1,A,I2,A,Z8,A,Z8,A,Z8)')
     &       'F_NEARSST it=',ITER,' s=',ISTNS,' i=',IBLNS,
     &       ' T=',TRANSFER(THET(IBLNS,ISTNS),1),
     &       ' D=',TRANSFER(DSTR(IBLNS,ISTNS),1),
     &       ' U=',TRANSFER(UEDG(IBLNS,ISTNS),1)
           ENDIF
 7852     CONTINUE
 7851   CONTINUE
C
C------ display changes and test for convergence
        IF(RLX.LT.1.0)
     &   WRITE(*,2000) ITER, RMSBL, RMXBL, VMXBL,IMXBL,ISMXBL,RLX
        IF(RLX.EQ.1.0)
     &   WRITE(*,2010) ITER, RMSBL, RMXBL, VMXBL,IMXBL,ISMXBL
C---- parity trace: per-iter station 67,68,69 side 2 BL state
        WRITE(0,'(A,I3,A,Z8.8,A,Z8.8,A,Z8.8,A,Z8.8)')
     &   'F_STN67S2 it=',ITER,
     &   ' T=',TRANSFER(THET(67,2),1),
     &   ' D=',TRANSFER(DSTR(67,2),1),
     &   ' U=',TRANSFER(UEDG(67,2),1),
     &   ' C=',TRANSFER(CTAU(67,2),1)
        WRITE(0,'(A,I3,A,Z8.8,A,Z8.8,A,Z8.8,A,Z8.8)')
     &   'F_STN68S2 it=',ITER,
     &   ' T=',TRANSFER(THET(68,2),1),
     &   ' D=',TRANSFER(DSTR(68,2),1),
     &   ' U=',TRANSFER(UEDG(68,2),1),
     &   ' C=',TRANSFER(CTAU(68,2),1)
        WRITE(0,'(A,I3,A,Z8.8,A,Z8.8,A,Z8.8,A,Z8.8)')
     &   'F_STN69S2 it=',ITER,
     &   ' T=',TRANSFER(THET(69,2),1),
     &   ' D=',TRANSFER(DSTR(69,2),1),
     &   ' U=',TRANSFER(UEDG(69,2),1),
     &   ' C=',TRANSFER(CTAU(69,2),1)
        WRITE(0,'(A,I3,A,Z8.8)')
     &   'F_RLX_ITER it=',ITER,' rlx=',TRANSFER(RLX,1)
C---- dump ALL stations both sides at iter 32 to find state-divergence origin
        IF(ITER.EQ.32 .OR. ITER.EQ.33) THEN
          DO 7877 ISD=1,2
          DO 7878 IBD=2,NBL(ISD)
            WRITE(0,'(A,I3,A,I1,A,I4,A,Z8.8,A,Z8.8,A,Z8.8,A,Z8.8,A,Z8.8)')
     &        'F_FULLBL it=',ITER,' s=',ISD,' i=',IBD,
     &        ' T=',TRANSFER(THET(IBD,ISD),1),
     &        ' D=',TRANSFER(DSTR(IBD,ISD),1),
     &        ' U=',TRANSFER(UEDG(IBD,ISD),1),
     &        ' C=',TRANSFER(CTAU(IBD,ISD),1),
     &        ' M=',TRANSFER(MASS(IBD,ISD),1)
 7878     CONTINUE
 7877     CONTINUE
        ENDIF
         CDP = CD - CDF
         WRITE(*,2020) ALFA/DTOR, CL, CM, CD, CDF, CDP
C
        IF(RMSBL .LT. EPS1) THEN
         LVCONV = .TRUE.
         AVISC = ALFA
         MVISC = MINF
         GO TO 90
        ENDIF
C
 1000 CONTINUE
      WRITE(*,*) 'VISCAL:  Convergence failed'
C
   90 CONTINUE
C---- emit hex CL/CD for reference generation
      WRITE(*,'(A,Z8.8,A,Z8.8)')
     & 'CDCALC_HEX CL=',TRANSFER(CL,1),' CD=',TRANSFER(CD,1)
      CALL CPCALC(N+NW,QINV,QINF,MINF,CPI)
      CALL CPCALC(N+NW,QVIS,QINF,MINF,CPV)
      IF(LFLAP) CALL MHINGE
      RETURN
C....................................................................
 2000   FORMAT
     &   (/1X,I3,'   rms: ',E10.4,'   max: ',E10.4,3X,A1,' at ',I4,I3,
     &     '   RLX:',F6.3)
 2010   FORMAT
     &   (/1X,I3,'   rms: ',E10.4,'   max: ',E10.4,3X,A1,' at ',I4,I3)
 2020   FORMAT
     &   ( 1X,3X,'   a =', F7.3,'      CL =',F8.4  /
     &     1X,3X,'  Cm =', F8.4, '     CD =',F9.5,
     &           '   =>   CDf =',F9.5,'    CDp =',F9.5)
      END ! VISCAL


      subroutine dcpout
      include 'XFOIL.INC'
c
c     Computes and writes upper and lower-surface 
c     Cp values at two specified x locations
c
c
      x1 = 0.05
      x2 = 0.15
c
      lu = 60
      open(lu,file='dcp.out',status='old',access='append',err=10)
      go to 20
c
 10   continue
      open(lu,file='dcp.out',status='new')
      write(lu,*) '#  ', name
      write(lu,*) '# alpha   CL       ',
     &            ' Cpl05     Cpu05     dCp05    ',
     &            ' Cpl15     Cpu15     dCp15    '
 20   continue
c
      call spline(cpv,w1,s,n)
c
      su1 = sle + x1*(s(1)-sle)
      sl1 = sle + x1*(s(n)-sle)
      su2 = sle + x2*(s(1)-sle)
      sl2 = sle + x2*(s(n)-sle)
c
      call sinvrt(sl1,x1,x,xp,s,n)
      call sinvrt(su1,x1,x,xp,s,n)
      call sinvrt(sl2,x2,x,xp,s,n)
      call sinvrt(su2,x2,x,xp,s,n)
c
      cpl1 = seval(sl1,cpv,w1,s,n)
      cpu1 = seval(su1,cpv,w1,s,n)
      cpl2 = seval(sl2,cpv,w1,s,n)
      cpu2 = seval(su2,cpv,w1,s,n)
c
      write(lu,1200) alfa/dtor, cl, 
     &               cpl1, cpu1, cpl1-cpu1,
     &               cpl2, cpu2, cpl2-cpu2

 1200 format(1x, f7.3, f9.4, 8f10.5)
c
      close(lu)
c
      return
      end
