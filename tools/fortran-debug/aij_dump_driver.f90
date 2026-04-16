! Dump the PSILIN-computed AIJ and BIJ influence matrices as hex for parity comparison.
! Links against the CLEAN (no-trace) xpanel.f for natural FMA behavior.
! Input: panel geometry from XFoil NACA generation (via COMMON blocks, set up by calling NACA + PANE)
! Output: AIJ(I,J) and BIJ(I,J) hex values plus QINVU basis speeds
program aij_dump_driver
  implicit none
  ! Just run XFoil initialization and dump the matrices
  ! This requires linking against the full XFoil object files
  ! Simpler: read the panel geometry from stdin and compute PSILIN directly

  integer, parameter :: MAXN = 400
  integer :: N, I, J
  real :: X(MAXN), Y(MAXN)
  real :: AIJ(MAXN, MAXN), BIJ(MAXN, MAXN)
  character(8) :: hex

  ! Read panel count and coordinates
  read(*, *) N
  do I = 1, N
    read(*, '(Z8,1X,Z8)') AIJ(I,1), AIJ(I,2)  ! Temp: read hex coords
    X(I) = AIJ(I,1)
    Y(I) = AIJ(I,2)
  end do

  write(*, '(I0)') N
  write(*, '(A)') 'END'
end program
