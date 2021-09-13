using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Threading;

namespace CL_IO
{
    /*
     * 추후 시간 날때 시뮬레이션 모드를 완성할것............ 
     * 현재 프로그래밍 중...................................
     */

    public enum eSolType
    {
        SINGLE,
        DOUBLE
    }

    public enum eSensType
    {
        ONE,
        TWO
    }



    class cSimulation_IO_List
    {
        public eSolType Out_Type = eSolType.SINGLE;
        public int[] Out_Index = new int[2];

        public eSensType In_Type = eSensType.ONE;
        public int[] In_Index = new int[2];

        public bool A_Type = true;  // A or B Type Set 

        public int DelayTime = 0;
        public int DelayCount = 0;
    }

    class cSimulation_Func
    {
        bool Simul_Flag = false;
        int Thread_DelayCount = 10;
        IList<cSimulation_IO_List> Check_List = new List<cSimulation_IO_List>();


        private void Func()
        {
            while (true)
            {
                if (Simul_Flag)
                {
                    foreach (cSimulation_IO_List _List in Check_List)
                    {
                        if (_List.Out_Type == eSolType.SINGLE)
                        {
                            // Sol Single Type
                            if (_List.In_Type == eSensType.ONE)
                            {
                                // Sensor One
                                if (_List.DelayCount >= _List.DelayTime)
                                {
                                    _List.DelayCount = 0;
                                }
                                else
                                {
                                    _List.DelayCount += 1;
                                }
                            }
                            else
                            {
                                // Sensor Two
                            }
                        }
                        else
                        {
                            // Sol Double Type
                            if (_List.In_Type == eSensType.ONE)
                            {
                                // Sensor One
                                if (_List.DelayCount >= _List.DelayTime)
                                {
                                    _List.DelayCount = 0;
                                }
                                else
                                {
                                    _List.DelayCount += 1;
                                }
                            }
                            else
                            {
                                // Sensor Two
                            }
                        }
                    }
                }

                Thread.Sleep(Thread_DelayCount);
            }
        }

        private void TypeFunc(cSimulation_IO_List iData)
        {

        }

        public void ListAdd()
        {

        }
    }

}
