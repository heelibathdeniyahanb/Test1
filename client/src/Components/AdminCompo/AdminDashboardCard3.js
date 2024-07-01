import React from 'react'
import { GiReceiveMoney } from "react-icons/gi";

export default function AdminDashboardCard3() {
  return (
    
      <div className="block max-w-md p-6 bg-white border border-gray-200 rounded-lg shadow hover:bg-gray-100 dark:bg-gray-800 dark:border-gray-700 dark:hover:bg-gray-700 ">
      
      <div ><GiReceiveMoney  className='text-white text-4xl' /></div>
      <h5 className="mb-2 text-2xl ml-8 font-bold tracking-tight text-gray-900 dark:text-white">
       Revenue
      </h5>
      <p className="font-normal text-gray-700 dark:text-gray-400 text-center text-3xl">
      10000$+
      </p> </div>
   
  );
}
  

