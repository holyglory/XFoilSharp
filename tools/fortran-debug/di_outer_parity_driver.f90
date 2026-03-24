program di_outer_parity_driver
  implicit none

  integer :: case_count
  integer :: case_index
  real :: s
  real :: hs
  real :: us
  real :: rt
  real :: hs_t
  real :: hs_d
  real :: hs_u
  real :: hs_ms
  real :: us_t
  real :: us_d
  real :: us_u
  real :: us_ms
  real :: rt_t
  real :: rt_u
  real :: rt_ms
  real :: dd
  real :: dd_hs
  real :: dd_us
  real :: dd_s
  real :: dd_t
  real :: dd_d
  real :: dd_u
  real :: dd_ms
  real :: ddl
  real :: ddl_hs
  real :: ddl_us
  real :: ddl_rt
  real :: ddl_t
  real :: ddl_d
  real :: ddl_u
  real :: ddl_ms

  read(*,*) case_count
  write(*,'(I8)') case_count

  do case_index = 1, case_count
    read(*,*) s, hs, us, rt
    read(*,*) hs_t, hs_d, hs_u, hs_ms
    read(*,*) us_t, us_d, us_u, us_ms
    read(*,*) rt_t, rt_u, rt_ms

    call compute_outer_di_terms(s, hs, us, rt, hs_t, hs_d, hs_u, hs_ms, &
      us_t, us_d, us_u, us_ms, rt_t, rt_u, rt_ms, &
      dd, dd_hs, dd_us, dd_s, dd_t, dd_d, dd_u, dd_ms, &
      ddl, ddl_hs, ddl_us, ddl_rt, ddl_t, ddl_d, ddl_u, ddl_ms)

    write(*,'(A,1X,I0,8(1X,Z8.8))') 'DD', case_index, &
      transfer(dd, 0), transfer(dd_hs, 0), transfer(dd_us, 0), transfer(dd_s, 0), &
      transfer(dd_t, 0), transfer(dd_d, 0), transfer(dd_u, 0), transfer(dd_ms, 0)
    write(*,'(A,1X,I0,8(1X,Z8.8))') 'DDL', case_index, &
      transfer(ddl, 0), transfer(ddl_hs, 0), transfer(ddl_us, 0), transfer(ddl_rt, 0), &
      transfer(ddl_t, 0), transfer(ddl_d, 0), transfer(ddl_u, 0), transfer(ddl_ms, 0)
  end do

contains

  subroutine compute_outer_di_terms(s, hs, us, rt, hs_t, hs_d, hs_u, hs_ms, &
      us_t, us_d, us_u, us_ms, rt_t, rt_u, rt_ms, &
      dd, dd_hs, dd_us, dd_s, dd_t, dd_d, dd_u, dd_ms, &
      ddl, ddl_hs, ddl_us, ddl_rt, ddl_t, ddl_d, ddl_u, ddl_ms)
    implicit none

    real, intent(in) :: s
    real, intent(in) :: hs
    real, intent(in) :: us
    real, intent(in) :: rt
    real, intent(in) :: hs_t
    real, intent(in) :: hs_d
    real, intent(in) :: hs_u
    real, intent(in) :: hs_ms
    real, intent(in) :: us_t
    real, intent(in) :: us_d
    real, intent(in) :: us_u
    real, intent(in) :: us_ms
    real, intent(in) :: rt_t
    real, intent(in) :: rt_u
    real, intent(in) :: rt_ms
    real, intent(out) :: dd
    real, intent(out) :: dd_hs
    real, intent(out) :: dd_us
    real, intent(out) :: dd_s
    real, intent(out) :: dd_t
    real, intent(out) :: dd_d
    real, intent(out) :: dd_u
    real, intent(out) :: dd_ms
    real, intent(out) :: ddl
    real, intent(out) :: ddl_hs
    real, intent(out) :: ddl_us
    real, intent(out) :: ddl_rt
    real, intent(out) :: ddl_t
    real, intent(out) :: ddl_d
    real, intent(out) :: ddl_u
    real, intent(out) :: ddl_ms

    dd = s**2 * (0.995 - us) * 2.0/hs
    dd_hs = -s**2 * (0.995 - us) * 2.0/hs**2
    dd_us = -s**2 * 2.0/hs
    dd_s = s*2.0 * (0.995-us) * 2.0/hs
    dd_t = dd_hs*hs_t + dd_us*us_t
    dd_d = dd_hs*hs_d + dd_us*us_d
    dd_u = dd_hs*hs_u + dd_us*us_u
    dd_ms = dd_hs*hs_ms + dd_us*us_ms

    ddl = 0.15*(0.995-us)**2 / rt * 2.0/hs
    ddl_us = -0.15*(0.995-us)*2.0 / rt * 2.0/hs
    ddl_hs = -ddl/hs
    ddl_rt = -ddl/rt
    ddl_t = ddl_hs*hs_t + ddl_us*us_t + ddl_rt*rt_t
    ddl_d = ddl_hs*hs_d + ddl_us*us_d
    ddl_u = ddl_hs*hs_u + ddl_us*us_u + ddl_rt*rt_u
    ddl_ms = ddl_hs*hs_ms + ddl_us*us_ms + ddl_rt*rt_ms
  end subroutine compute_outer_di_terms

end program di_outer_parity_driver
