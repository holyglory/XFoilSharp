program di_wall_parity_driver
  use bl_common_kernels, only: cft_kernel
  implicit none

  integer :: case_count
  integer :: case_index
  real :: hk
  real :: hs
  real :: us
  real :: rt
  real :: msq
  real :: hk_t
  real :: hk_d
  real :: hk_u
  real :: hk_ms
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
  real :: m_u
  real :: m_ms
  real :: cf2t
  real :: cf2t_hk
  real :: cf2t_rt
  real :: cf2t_m
  real :: cf2t_t
  real :: cf2t_d
  real :: cf2t_u
  real :: cf2t_ms
  real :: di
  real :: di_hs
  real :: di_us
  real :: di_cf2t
  real :: di_t
  real :: di_d
  real :: di_u
  real :: di_ms

  read(*,*) case_count
  write(*,'(I8)') case_count

  do case_index = 1, case_count
    read(*,*) hk, hs, us, rt, msq
    read(*,*) hk_t, hk_d, hk_u, hk_ms
    read(*,*) hs_t, hs_d, hs_u, hs_ms
    read(*,*) us_t, us_d, us_u, us_ms
    read(*,*) rt_t, rt_u, rt_ms
    read(*,*) m_u, m_ms

    call compute_di_wall(hk, hs, us, rt, msq, hk_t, hk_d, hk_u, hk_ms, &
      hs_t, hs_d, hs_u, hs_ms, us_t, us_d, us_u, us_ms, &
      rt_t, rt_u, rt_ms, m_u, m_ms, &
      cf2t, cf2t_hk, cf2t_rt, cf2t_m, cf2t_t, cf2t_d, cf2t_u, cf2t_ms, &
      di, di_hs, di_us, di_cf2t, di_t, di_d, di_u, di_ms)

    write(*,'(A,1X,I0,8(1X,Z8.8))') 'CF', case_index, &
      transfer(cf2t, 0), transfer(cf2t_hk, 0), transfer(cf2t_rt, 0), transfer(cf2t_m, 0), &
      transfer(cf2t_t, 0), transfer(cf2t_d, 0), transfer(cf2t_u, 0), transfer(cf2t_ms, 0)
    write(*,'(A,1X,I0,8(1X,Z8.8))') 'DI', case_index, &
      transfer(di, 0), transfer(di_hs, 0), transfer(di_us, 0), transfer(di_cf2t, 0), &
      transfer(di_t, 0), transfer(di_d, 0), transfer(di_u, 0), transfer(di_ms, 0)
  end do

contains

  subroutine compute_di_wall(hk, hs, us, rt, msq, hk_t, hk_d, hk_u, hk_ms, &
      hs_t, hs_d, hs_u, hs_ms, us_t, us_d, us_u, us_ms, &
      rt_t, rt_u, rt_ms, m_u, m_ms, &
      cf2t, cf2t_hk, cf2t_rt, cf2t_m, cf2t_t, cf2t_d, cf2t_u, cf2t_ms, &
      di, di_hs, di_us, di_cf2t, di_t, di_d, di_u, di_ms)
    implicit none

    real, intent(in) :: hk
    real, intent(in) :: hs
    real, intent(in) :: us
    real, intent(in) :: rt
    real, intent(in) :: msq
    real, intent(in) :: hk_t
    real, intent(in) :: hk_d
    real, intent(in) :: hk_u
    real, intent(in) :: hk_ms
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
    real, intent(in) :: m_u
    real, intent(in) :: m_ms
    real, intent(out) :: cf2t
    real, intent(out) :: cf2t_hk
    real, intent(out) :: cf2t_rt
    real, intent(out) :: cf2t_m
    real, intent(out) :: cf2t_t
    real, intent(out) :: cf2t_d
    real, intent(out) :: cf2t_u
    real, intent(out) :: cf2t_ms
    real, intent(out) :: di
    real, intent(out) :: di_hs
    real, intent(out) :: di_us
    real, intent(out) :: di_cf2t
    real, intent(out) :: di_t
    real, intent(out) :: di_d
    real, intent(out) :: di_u
    real, intent(out) :: di_ms

    call cft_kernel(hk, rt, msq, cf2t, cf2t_hk, cf2t_rt, cf2t_m)
    cf2t_u = cf2t_hk*hk_u + cf2t_rt*rt_u + cf2t_m*m_u
    cf2t_t = cf2t_hk*hk_t + cf2t_rt*rt_t
    cf2t_d = cf2t_hk*hk_d
    cf2t_ms = cf2t_hk*hk_ms + cf2t_rt*rt_ms + cf2t_m*m_ms

    di = (0.5*cf2t*us) * 2.0/hs
    di_hs = -(0.5*cf2t*us) * 2.0/hs**2
    di_us = (0.5*cf2t) * 2.0/hs
    di_cf2t = (0.5*us) * 2.0/hs
    di_t = di_hs*hs_t + di_us*us_t + di_cf2t*cf2t_t
    di_d = di_hs*hs_d + di_us*us_d + di_cf2t*cf2t_d
    di_u = di_hs*hs_u + di_us*us_u + di_cf2t*cf2t_u
    di_ms = di_hs*hs_ms + di_us*us_ms + di_cf2t*cf2t_ms
  end subroutine compute_di_wall

end program di_wall_parity_driver
