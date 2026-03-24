program dil_parity_driver
  implicit none

  integer :: case_count
  integer :: case_index
  real :: hk
  real :: rt
  real :: hk_t
  real :: hk_d
  real :: hk_u
  real :: hk_ms
  real :: rt_t
  real :: rt_u
  real :: rt_ms
  real :: di
  real :: di_hk
  real :: di_rt
  real :: di_t
  real :: di_d
  real :: di_u
  real :: di_ms

  read(*,*) case_count
  write(*,'(I8)') case_count

  do case_index = 1, case_count
    read(*,*) hk, rt
    read(*,*) hk_t, hk_d, hk_u, hk_ms
    read(*,*) rt_t, rt_u, rt_ms

    call compute_dil_chain(hk, rt, hk_t, hk_d, hk_u, hk_ms, rt_t, rt_u, rt_ms, &
      di, di_hk, di_rt, di_t, di_d, di_u, di_ms)

    write(*,'(A,1X,I0,3(1X,Z8.8))') 'TERMS', case_index, transfer(di, 0), transfer(di_hk, 0), transfer(di_rt, 0)
    write(*,'(A,1X,I0,5(1X,Z8.8))') 'FINAL', case_index, &
      transfer(di, 0), transfer(di_t, 0), transfer(di_d, 0), transfer(di_u, 0), transfer(di_ms, 0)
  end do

contains

  subroutine compute_dil_chain(hk, rt, hk_t, hk_d, hk_u, hk_ms, rt_t, rt_u, rt_ms, &
      di, di_hk, di_rt, di_t, di_d, di_u, di_ms)
    implicit none

    real, intent(in) :: hk
    real, intent(in) :: rt
    real, intent(in) :: hk_t
    real, intent(in) :: hk_d
    real, intent(in) :: hk_u
    real, intent(in) :: hk_ms
    real, intent(in) :: rt_t
    real, intent(in) :: rt_u
    real, intent(in) :: rt_ms
    real, intent(out) :: di
    real, intent(out) :: di_hk
    real, intent(out) :: di_rt
    real, intent(out) :: di_t
    real, intent(out) :: di_d
    real, intent(out) :: di_u
    real, intent(out) :: di_ms
    real :: hkb
    real :: hkb_sq
    real :: den
    real :: ratio
    real :: numer

    hkb = 0.0
    hkb_sq = 0.0
    den = 0.0
    ratio = 0.0
    numer = 0.0

    if (hk .lt. 4.0) then
      numer = 0.00205 * (4.0 - hk)**5.5 + 0.207
      di = numer / rt
      di_hk = (-0.00205*5.5*(4.0 - hk)**4.5) / rt
    else
      hkb = hk - 4.0
      hkb_sq = hkb**2
      den = 1.0 + 0.02*hkb_sq
      ratio = hkb_sq / den
      numer = -0.0016*ratio + 0.207
      di = numer / rt
      di_hk = (-0.0016*2.0*hkb*(1.0/den - 0.02*hkb**2/den**2)) / rt
    end if

    di_rt = -di / rt
    di_t = di_hk*hk_t + di_rt*rt_t
    di_d = di_hk*hk_d
    di_u = di_hk*hk_u + di_rt*rt_u
    di_ms = di_hk*hk_ms + di_rt*rt_ms
  end subroutine compute_dil_chain

end program dil_parity_driver
