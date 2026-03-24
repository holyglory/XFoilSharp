      PROGRAM HST_PARITY_DRIVER
      IMPLICIT NONE
      INTEGER CASECOUNT, CASEINDEX
      REAL HK, RT, MSQ
      REAL HS, HS_HK, HS_RT, HS_MSQ
      REAL HSBITVALUE, HSHKBITVALUE, HSRTBITVALUE, HSMSQBITVALUE
      INTEGER HSBITS, HSHKBITS, HSRTBITS, HSMSQBITS
      EQUIVALENCE (HSBITVALUE, HSBITS)
      EQUIVALENCE (HSHKBITVALUE, HSHKBITS)
      EQUIVALENCE (HSRTBITVALUE, HSRTBITS)
      EQUIVALENCE (HSMSQBITVALUE, HSMSQBITS)
C
      READ(*,*) CASECOUNT
      WRITE(*,'(I8)') CASECOUNT
C
      DO 100 CASEINDEX = 1, CASECOUNT
        READ(*,*) HK, RT, MSQ
        CALL HST(HK, RT, MSQ, HS, HS_HK, HS_RT, HS_MSQ)
        HSBITVALUE = HS
        HSHKBITVALUE = HS_HK
        HSRTBITVALUE = HS_RT
        HSMSQBITVALUE = HS_MSQ
        WRITE(*,1000) HSBITS, HS, HSHKBITS, HS_HK,
     &                HSRTBITS, HS_RT, HSMSQBITS, HS_MSQ
 1000   FORMAT(Z8.8,1X,1PE24.16,1X,Z8.8,1X,1PE24.16,1X,
     &         Z8.8,1X,1PE24.16,1X,Z8.8,1X,1PE24.16)
  100 CONTINUE
C
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
C----- attached branch
       HR    = ( HO - HK)/(HO-1.0)
       HR_HK =      - 1.0/(HO-1.0)
       HR_RT = (1.0 - HR)/(HO-1.0) * HO_RT
       HS    = (2.0-HSMIN-4.0/RTZ)*HR**2  * 1.5/(HK+0.5) + HSMIN
     &       + 4.0/RTZ
       HS_HK =-(2.0-HSMIN-4.0/RTZ)*HR**2  * 1.5/(HK+0.5)**2
     &       + (2.0-HSMIN-4.0/RTZ)*HR*2.0 * 1.5/(HK+0.5) * HR_HK
       HS_RT = (2.0-HSMIN-4.0/RTZ)*HR*2.0 * 1.5/(HK+0.5) * HR_RT
     &       + (HR**2 * 1.5/(HK+0.5) - 1.0)*4.0/RTZ**2 * RTZ_RT
C
      ELSE
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
       HS_RT = HDIF**2 * HTMP_RT
     &       - 4.0/RTZ**2 * RTZ_RT
     &       + HDIF*2.0* HTMP * (-HO_RT)
C
      ENDIF
C
C---- Whitfield's minor additional compressibility correction
      FM = 1.0 + 0.014*MSQ
      HS     = ( HS + 0.028*MSQ ) / FM
      HS_HK  = ( HS_HK          ) / FM
      HS_RT  = ( HS_RT          ) / FM
      HS_MSQ = 0.028/FM  -  0.014*HS/FM
C
      RETURN
      END
